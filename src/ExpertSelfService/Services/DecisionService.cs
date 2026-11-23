using Dapper;
using Microsoft.Data.Sqlite;

namespace ExpertSelfService.Services;

public enum DecisionOutcome
{
    Done,
    AlreadyDecided,
    NotFound,
}

/// <summary>What happened, and for a decision just made: whose change and which field, for the message.</summary>
public sealed record DecisionResult(DecisionOutcome Outcome, string ExpertName = "", string Field = "");

/// <summary>
/// Story S2: the secretariat approves or rejects one pending change (SPEC section 3).
/// Approve writes the new value (Candidates or CandidateSkills), LastUpdated and one change-log row. Reject saves the
/// reason only. Both run in one write transaction that reads the row again, so a change is decided once only, even
/// with two tabs or a double click.
/// </summary>
public sealed class DecisionService(Database database, ExpertChanges changes, TimeProvider clock)
{
    /// <summary>The longest reason (SPEC S-B).</summary>
    public const int MaxReason = 500;

    private sealed record StatusRow(long ExpertId, string Status);

    private sealed record PendingRow(long ExpertId, string Field, string? OldValue, string? NewValue, string Status, string FirstName, string? MiddleName, string LastName);

    /// <summary>
    /// The reason as measured and stored: every line break as one \n, trimmed. The browser sends \r\n but its
    /// maxlength counts a line break as one character, so the box and the check agree (review finding #2).
    /// </summary>
    public static string CleanReason(string reason) =>
        reason.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    /// <summary>The i18n key of what is wrong with a reason, or null when it is fine. Line breaks are allowed.</summary>
    public static string? ReasonError(string? reason)
    {
        var text = CleanReason(reason ?? "");
        if (text.Length == 0)
        {
            return "pending.error.reason_required";
        }

        if (TextRules.HasControlCharacter(text.Replace("\n", "", StringComparison.Ordinal)))
        {
            return "pending.error.reason_control";
        }

        return text.Length > MaxReason ? "pending.error.reason_too_long" : null;
    }

    /// <summary>
    /// Done when the change is still pending (it can be decided), else AlreadyDecided or NotFound.
    /// With <paramref name="expertId"/> (S-C), a change that belongs to another expert is NotFound.
    /// </summary>
    public DecisionOutcome StatusOf(long changeId, long? expertId = null)
    {
        using var db = database.Open();
        var row = db.QuerySingleOrDefault<StatusRow>(
            "SELECT candidate_id AS ExpertId, status AS Status FROM ss_pending_changes WHERE id = @changeId", new { changeId });
        if (row is null || !Belongs(row.ExpertId, expertId))
        {
            return DecisionOutcome.NotFound;
        }

        return row.Status == "pending" ? DecisionOutcome.Done : DecisionOutcome.AlreadyDecided;
    }

    /// <summary>With <paramref name="expertId"/> (S-C), a change that belongs to another expert is NotFound.</summary>
    public DecisionResult Approve(long changeId, long staffId, long? expertId = null)
    {
        using var db = database.Open();
        // Not deferred: the write lock is taken at the start, so two decisions on one row run one after the other.
        // Decide's row count is the second guard, should that ever change.
        using var transaction = db.BeginTransaction();
        var row = Read(db, transaction, changeId);
        if (row is null || !Belongs(row.ExpertId, expertId))
        {
            return new DecisionResult(DecisionOutcome.NotFound);
        }

        if (row.Status != "pending")
        {
            return new DecisionResult(DecisionOutcome.AlreadyDecided);
        }

        var (oldValue, newValue) = row.Field == SkillRules.Field
            ? ApplySkills(db, transaction, row.ExpertId, row.OldValue, row.NewValue)
            : (ApplyName(db, transaction, row.ExpertId, row.Field, row.NewValue), row.NewValue);

        // Nothing changed when the value was already in place: the change is still decided, but no log row is
        // written and LastUpdated stays, one row per changed field (SPEC section 3, review finding #3).
        IReadOnlyList<FieldChange> changed = string.Equals(oldValue, newValue, StringComparison.Ordinal)
            ? []
            : [new FieldChange(row.Field, oldValue, newValue)];
        changes.Record(db, transaction, row.ExpertId, ExpertChanges.ByStaff, staffId, changed, changeId);

        // Not committed: disposing the transaction undoes the write above and the log row.
        if (!Decide(db, transaction, changeId, "approved", null, staffId))
        {
            return new DecisionResult(DecisionOutcome.AlreadyDecided);
        }

        transaction.Commit();
        return Done(row);
    }

    /// <summary>
    /// The caller has checked the reason with <see cref="ReasonError"/>.
    /// With <paramref name="expertId"/> (S-C), a change that belongs to another expert is NotFound.
    /// </summary>
    public DecisionResult Reject(long changeId, long staffId, string reason, long? expertId = null)
    {
        using var db = database.Open();
        using var transaction = db.BeginTransaction();
        var row = Read(db, transaction, changeId);
        if (row is null || !Belongs(row.ExpertId, expertId))
        {
            return new DecisionResult(DecisionOutcome.NotFound);
        }

        if (row.Status != "pending")
        {
            return new DecisionResult(DecisionOutcome.AlreadyDecided);
        }

        // Candidates and the change log are not touched: nothing changed (SPEC section 3).
        if (!Decide(db, transaction, changeId, "rejected", CleanReason(reason), staffId))
        {
            return new DecisionResult(DecisionOutcome.AlreadyDecided);
        }

        transaction.Commit();
        return Done(row);
    }

    // S-B passes no expert (any change); S-C passes the expert in its URL, and only that expert's changes count.
    private static bool Belongs(long changeExpertId, long? expertId) => expertId is null || changeExpertId == expertId;

    private static DecisionResult Done(PendingRow row) =>
        new(DecisionOutcome.Done, ExpertName.Full(row.FirstName, row.MiddleName, row.LastName), row.Field);

    // A Retired expert's change can still be decided: the secretariat owns the record.
    private static PendingRow? Read(SqliteConnection db, SqliteTransaction transaction, long changeId) =>
        db.QuerySingleOrDefault<PendingRow>(
            """
            SELECT p.candidate_id AS ExpertId, p.field AS Field, p.old_value AS OldValue, p.new_value AS NewValue, p.status AS Status,
                   c.FirstName, c.MiddleName, c.LastName
            FROM ss_pending_changes p
            JOIN Candidates c ON c.Id = p.candidate_id
            WHERE p.id = @changeId
            """,
            new { changeId },
            transaction);

    /// <summary>
    /// True when this call decided the change. The second guard after the write lock: if the row was no longer
    /// pending, no row changes, and the caller returns without committing.
    /// </summary>
    private bool Decide(SqliteConnection db, SqliteTransaction transaction, long changeId, string status, string? reason, long staffId) =>
        db.Execute(
            """
            UPDATE ss_pending_changes
            SET status = @status, reason = @reason, decided_by = @staffId, decided_at = @decidedAt
            WHERE id = @changeId AND status = 'pending'
            """,
            new { changeId, status, reason, staffId, decidedAt = Database.ToText(clock.GetUtcNow()) },
            transaction) == 1;

    /// <summary>
    /// Writes one name field and returns the value it replaced: what Candidates held now, not what the expert saw
    /// when sending, so the log shows what was really replaced. The column is picked from a fixed list, never
    /// glued in from data.
    /// </summary>
    private static string? ApplyName(SqliteConnection db, SqliteTransaction transaction, long expertId, string field, string? newValue)
    {
        var (select, update) = field switch
        {
            NameRules.FirstName => (
                "SELECT FirstName FROM Candidates WHERE Id = @expertId",
                "UPDATE Candidates SET FirstName = @newValue WHERE Id = @expertId"),
            NameRules.MiddleName => (
                "SELECT MiddleName FROM Candidates WHERE Id = @expertId",
                "UPDATE Candidates SET MiddleName = @newValue WHERE Id = @expertId"),
            NameRules.LastName => (
                "SELECT LastName FROM Candidates WHERE Id = @expertId",
                "UPDATE Candidates SET LastName = @newValue WHERE Id = @expertId"),
            _ => throw new InvalidOperationException($"Unknown pending field '{field}'"),
        };

        var oldValue = db.ExecuteScalar<string?>(select, new { expertId }, transaction);
        db.Execute(update, new { expertId, newValue = TextRules.Clean(newValue) }, transaction);
        return oldValue;
    }

    /// <summary>
    /// Applies exactly what S-B showed: the Added and Removed between the list the expert had when sending and the
    /// list they sent (<see cref="SkillRules.Diff"/>). A skill that came or went in between is left alone, a removed
    /// skill already gone or an added one already there is skipped, and unchanged rows keep their Id.
    /// Returns the list before and after, as stored, for the change log.
    /// </summary>
    private static (string Old, string New) ApplySkills(SqliteConnection db, SqliteTransaction transaction, long expertId, string? oldValue, string? newValue)
    {
        var current = CurrentSkills(db, transaction, expertId);
        var (added, removed) = SkillRules.Diff(oldValue, newValue);
        var gone = removed.ToHashSet(StringComparer.Ordinal);
        var have = current.Select(row => row.Skill).OfType<string>().ToHashSet(StringComparer.Ordinal);

        foreach (var row in current.Where(row => row.Skill is not null && gone.Contains(row.Skill)))
        {
            db.Execute("DELETE FROM CandidateSkills WHERE Id = @Id", new { row.Id }, transaction);
        }

        foreach (var skill in added.Where(skill => !have.Contains(skill)))
        {
            db.Execute(
                "INSERT INTO CandidateSkills (CandidateId, Skill) VALUES (@expertId, @skill)",
                new { expertId, skill },
                transaction);
        }

        var after = CurrentSkills(db, transaction, expertId);
        return (SkillRules.Join(current.Select(row => row.Skill)), SkillRules.Join(after.Select(row => row.Skill)));
    }

    private static List<SkillRow> CurrentSkills(SqliteConnection db, SqliteTransaction transaction, long expertId) =>
        [.. db.Query<SkillRow>(
            "SELECT Id, Skill FROM CandidateSkills WHERE CandidateId = @expertId",
            new { expertId },
            transaction)];

    private sealed record SkillRow(long Id, string? Skill);
}
