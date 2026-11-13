using Dapper;
using ExpertSelfService.Models;
using Microsoft.Data.Sqlite;

namespace ExpertSelfService.Services;

/// <summary>
/// Story E7, screen X9: the fields of expertise go to approval (SPEC section 3). One ss_pending_changes row with
/// the whole old and new list; CandidateSkills, LastUpdated and ss_change_log are left alone until S2 decides.
/// </summary>
public sealed class SkillService(Database database, TimeProvider clock)
{
    /// <summary>The page with the current fields ticked, or null when the expert is gone or Retired.</summary>
    public SkillsPage? Load(long expertId)
    {
        using var db = database.Open();
        // Deferred: a read transaction, so the fields, the list and the state are from the same moment.
        using var transaction = db.BeginTransaction(deferred: true);
        if (!Exists(db, transaction, expertId))
        {
            return null;
        }

        var current = CurrentSkills(db, transaction, expertId);
        var fags = SkillRules.Sorted(db.Query<string>("SELECT Fag FROM Fags", transaction: transaction));
        var state = ProfileService.NewestStates(db, transaction, expertId).GetValueOrDefault(SkillRules.Field);
        transaction.Commit();

        return new SkillsPage(current, fags, current, state, null);
    }

    /// <summary>
    /// Writes one pending row when the chosen set differs from the current one. A pending change is checked again
    /// here, inside the write transaction, so a second tab or a hand-made post can never add a second one.
    /// The caller has already checked that every name is allowed and that at least one is chosen.
    /// </summary>
    public SendResult Send(long expertId, IEnumerable<string> chosen)
    {
        using var db = database.Open();
        // Not deferred: the write lock is taken at the start, so two sends at once run one after the other.
        using var transaction = db.BeginTransaction();
        if (!Exists(db, transaction, expertId))
        {
            return SendResult.ExpertNotFound;
        }

        if (ProfileService.NewestStates(db, transaction, expertId).GetValueOrDefault(SkillRules.Field)?.IsPending == true)
        {
            return SendResult.NothingChanged;
        }

        // A set: order and duplicates don't count, so the joined, sorted text is the comparison.
        var oldValue = SkillRules.Join(CurrentSkills(db, transaction, expertId));
        var newValue = SkillRules.Join(chosen);
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return SendResult.NothingChanged;
        }

        db.Execute(
            """
            INSERT INTO ss_pending_changes (candidate_id, field, old_value, new_value, status, created_at)
            VALUES (@expertId, @field, @oldValue, @newValue, 'pending', @createdAt)
            """,
            new { expertId, field = SkillRules.Field, oldValue, newValue, createdAt = Database.ToText(clock.GetUtcNow()) },
            transaction);

        transaction.Commit();
        return SendResult.Sent;
    }

    private static bool Exists(SqliteConnection db, SqliteTransaction transaction, long expertId) =>
        db.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM Candidates WHERE Id = @expertId AND Status <> @retired",
            new { expertId, retired = ExpertStatus.Retired },
            transaction) == 1;

    // Empty and duplicate rows are left out, as on the profile.
    private static List<string> CurrentSkills(SqliteConnection db, SqliteTransaction transaction, long expertId) =>
        SkillRules.Sorted(db.Query<string?>(
            "SELECT Skill FROM CandidateSkills WHERE CandidateId = @expertId",
            new { expertId },
            transaction));
}
