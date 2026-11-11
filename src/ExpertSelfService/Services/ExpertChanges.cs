using Dapper;
using Microsoft.Data.Sqlite;

namespace ExpertSelfService.Services;

/// <summary>One changed field: its name and the old and new value as the change log shows them.</summary>
public sealed record FieldChange(string Field, string? OldValue, string? NewValue);

/// <summary>
/// The rule for every change to an expert's data (SPEC section 3): Candidates.LastUpdated is set to today,
/// and one ss_change_log row is written per changed field, in the caller's transaction. So if the log fails,
/// the change itself is rolled back too. E3 is the first user; E4, E6, E8 and S2 (approve) use it as well.
/// </summary>
public sealed class ExpertChanges(TimeProvider clock)
{
    public const string ByExpert = "expert";
    public const string ByStaff = "staff";

    public void Record(
        SqliteConnection db, SqliteTransaction transaction, long expertId,
        string actorType, long actorId, IReadOnlyList<FieldChange> changes, long? pendingChangeId = null)
    {
        if (changes.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        db.Execute(
            "UPDATE Candidates SET LastUpdated = @today WHERE Id = @expertId",
            new { today = LocalTime.DateText(now), expertId },
            transaction);

        foreach (var change in changes)
        {
            db.Execute(
                """
                INSERT INTO ss_change_log (candidate_id, actor_type, actor_id, field, old_value, new_value, pending_change_id, created_at)
                VALUES (@expertId, @actorType, @actorId, @Field, @OldValue, @NewValue, @pendingChangeId, @createdAt)
                """,
                new
                {
                    expertId,
                    actorType,
                    actorId,
                    change.Field,
                    change.OldValue,
                    change.NewValue,
                    pendingChangeId,
                    createdAt = Database.ToText(now),
                },
                transaction);
        }
    }
}
