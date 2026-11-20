using Dapper;

namespace ExpertSelfService.Services;

/// <summary>One change-log entry as stored. StaffName is set when a secretariat user made the change.</summary>
public sealed record ChangeLogEntry(
    string CreatedAt,
    string ActorType,
    long ActorId,
    string? StaffName,
    string Field,
    string? OldValue,
    string? NewValue);

/// <summary>One page of an expert's change log, and how many entries there are in all.</summary>
public sealed record ChangeLogPage(int Total, IReadOnlyList<ChangeLogEntry> Entries);

/// <summary>
/// Story S3, screen S-C: an expert's change log, newest first. Read-only: this class only selects. ss_change_log is
/// append-only, and its triggers refuse every UPDATE and DELETE (db/schema_sqlite.sql).
/// </summary>
public sealed class ChangeLogService(Database database)
{
    /// <summary>Entries per page on S-C.</summary>
    public const int PageSize = 10;

    public int Count(long expertId)
    {
        using var db = database.Open();
        return db.ExecuteScalar<int>("SELECT COUNT(*) FROM ss_change_log WHERE candidate_id = @expertId", new { expertId });
    }

    /// <summary>
    /// Page <paramref name="page"/> (1 is the newest). Newest first; two entries written at the same moment (one save
    /// that changed several fields) come newest id first, so the order never changes between page loads.
    /// </summary>
    public ChangeLogPage Page(long expertId, int page)
    {
        using var db = database.Open();
        using var transaction = db.BeginTransaction(deferred: true);
        var total = db.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM ss_change_log WHERE candidate_id = @expertId", new { expertId }, transaction);
        var entries = db.Query<ChangeLogEntry>(
            """
            SELECT l.created_at AS CreatedAt, l.actor_type AS ActorType, l.actor_id AS ActorId,
                   s.name AS StaffName, l.field AS Field, l.old_value AS OldValue, l.new_value AS NewValue
            FROM ss_change_log l
            LEFT JOIN ss_staff_users s ON l.actor_type = 'staff' AND s.id = l.actor_id
            WHERE l.candidate_id = @expertId
            ORDER BY l.created_at DESC, l.id DESC
            LIMIT @limit OFFSET @offset
            """,
            new { expertId, limit = PageSize, offset = (page - 1) * PageSize },
            transaction).ToList();
        transaction.Commit();
        return new ChangeLogPage(total, entries);
    }
}
