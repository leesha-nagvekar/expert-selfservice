using Dapper;

namespace ExpertSelfService.Services;

/// <summary>One waiting change on S-B, with the expert it belongs to.</summary>
public sealed record PendingItem(
    long Id,
    long ExpertId,
    string ExpertName,
    string? RegistrationId,
    string Field,
    string? OldValue,
    string? NewValue,
    string CreatedAt);

/// <summary>
/// Story S2, screen S-B: the waiting changes, oldest first. Read-only. Notes, Ratings, street and phone numbers are
/// never selected.
/// </summary>
public sealed class PendingListService(Database database)
{
    /// <summary>The pending changes whose expert matches every word of the search box (SearchRules.AllWordsMatch).</summary>
    public List<PendingItem> List(string? query)
    {
        var words = SearchRules.Words(query);
        using var db = database.Open();
        var rows = db.Query<Row>(
            """
            SELECT p.id AS Id, p.candidate_id AS ExpertId, c.FirstName, c.MiddleName, c.LastName, c.Email,
                   c.RegistrationId, p.field AS Field, p.old_value AS OldValue, p.new_value AS NewValue,
                   p.created_at AS CreatedAt
            FROM ss_pending_changes p
            JOIN Candidates c ON c.Id = p.candidate_id
            WHERE p.status = 'pending'
            ORDER BY p.created_at, p.id
            """);

        return [.. rows
            .Where(row => SearchRules.AllWordsMatch(
                words, row.RegistrationId, row.FirstName, row.MiddleName, row.LastName, row.Email))
            .Select(row => new PendingItem(
                row.Id,
                row.ExpertId,
                ExpertName.Full(row.FirstName, row.MiddleName, row.LastName),
                row.RegistrationId,
                row.Field,
                row.OldValue,
                row.NewValue,
                row.CreatedAt))];
    }

    /// <summary>How many changes are waiting, for "Pending approvals (N)" in the top bar.</summary>
    public int Count()
    {
        using var db = database.Open();
        return db.ExecuteScalar<int>("SELECT COUNT(*) FROM ss_pending_changes WHERE status = 'pending'");
    }

    private sealed class Row
    {
        public long Id { get; init; }

        public long ExpertId { get; init; }

        public string FirstName { get; init; } = "";

        public string? MiddleName { get; init; }

        public string LastName { get; init; } = "";

        public string? Email { get; init; }

        public string? RegistrationId { get; init; }

        public string Field { get; init; } = "";

        public string? OldValue { get; init; }

        public string? NewValue { get; init; }

        public string CreatedAt { get; init; } = "";
    }
}
