using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ExpertSelfService.Services;

/// <summary>One value in a sidebar list: what the query string carries, and what the page shows.</summary>
public sealed record ListValue(string Value, string Name);

/// <summary>The values the sidebar offers. Region, role and field names are VBA's own, from the database.</summary>
public sealed record SearchLists(
    IReadOnlyList<ListValue> Regions,
    IReadOnlyList<ListValue> Roles,
    IReadOnlyList<ListValue> Fields);

/// <summary>
/// What the secretariat searched for. An empty list means "all": no tick in that group (SPEC S-A).
/// </summary>
public sealed record SearchFilters(
    IReadOnlyList<string> Words,
    IReadOnlyList<int> Regions,
    IReadOnlyList<int> Roles,
    IReadOnlyList<string> Fields,
    IReadOnlyList<int> Statuses,
    bool PendingOnly);

/// <summary>One expert in the results.</summary>
public sealed record ExpertHit(
    long Id,
    string Name,
    string? RegistrationId,
    string? Postcode,
    string? City,
    int Status,
    int PendingCount);

/// <summary>What came in on the query string, before it is checked.</summary>
public sealed record SearchRequest(
    string? Query,
    string?[] Regions,
    string?[] Roles,
    string?[] Fields,
    string?[] Statuses,
    string? Pending);

/// <summary>
/// One search: the sidebar's lists, the checked filters, and the experts found. Hits is null when the search
/// text is too long, so no search was run.
/// </summary>
public sealed record SearchOutcome(SearchLists Lists, SearchFilters Filters, IReadOnlyList<ExpertHit>? Hits);

/// <summary>
/// Story S1, screen S-A. Read-only. Notes, Ratings, street and phone numbers are never selected.
/// </summary>
public sealed class ExpertSearchService(Database database)
{
    /// <summary>
    /// The lists, the check against them and the search, in one read transaction, so they all see the same moment:
    /// a value is checked against the same lists that the search and the page use. Null when a value isn't in its
    /// list (a hand-made URL, a 400).
    /// </summary>
    public SearchOutcome? Search(SearchRequest request)
    {
        using var db = database.Open();
        // Deferred: a read transaction that doesn't take the write lock, the same as ProfileService.Load.
        using var transaction = db.BeginTransaction(deferred: true);

        var lists = Lists(db, transaction);
        var filters = SearchRules.Read(
            lists, request.Query, request.Regions, request.Roles, request.Fields, request.Statuses, request.Pending);
        if (filters is null)
        {
            return null;
        }

        var hits = SearchRules.IsTooLong(request.Query) ? null : Find(db, transaction, filters);
        transaction.Commit();
        return new SearchOutcome(lists, filters, hits);
    }

    /// <summary>The sidebar's lists: the five Danish regions in VBA's order, and roles and fields in Danish order.</summary>
    private static SearchLists Lists(SqliteConnection db, SqliteTransaction transaction)
    {
        // Experts can only have Danish postcodes (AddressRules.DanishRegionIds), so other regions would always be empty.
        var regions = db.Query<ListRow>(
            """
            SELECT CAST(Id AS TEXT) AS Value, RegionName AS Name
            FROM Regions
            WHERE Id IN @danish
            ORDER BY DisplayOrder, Id
            """,
            new { danish = AddressRules.DanishRegionIds },
            transaction);
        var roles = db.Query<ListRow>("SELECT CAST(Id AS TEXT) AS Value, Tag AS Name FROM ProfessionTags", transaction: transaction);
        var fields = db.Query<string>("SELECT DISTINCT Fag FROM Fags WHERE Fag <> ''", transaction: transaction);

        return new SearchLists(
            [.. regions.Select(row => new ListValue(row.Value, row.Name))],
            [.. roles
                .OrderBy(row => row.Name, DanishOrder.Comparer)
                .ThenBy(row => row.Name, StringComparer.Ordinal)
                .Select(row => new ListValue(row.Value, row.Name))],
            [.. SkillRules.Sorted(fields).Select(name => new ListValue(name, name))]);
    }

    /// <summary>The experts that match every filter, sorted by last name, then first name, in Danish order.</summary>
    private static List<ExpertHit> Find(SqliteConnection db, SqliteTransaction transaction, SearchFilters filters)
    {
        // The sidebar groups: fixed SQL, every value a Dapper parameter. Dapper turns "IN @regions" into one
        // parameter per value. A flag per group says whether anything is ticked, so an empty group means "all".
        var rows = db.Query<HitRow>(
            """
            SELECT c.Id, c.FirstName, c.MiddleName, c.LastName, c.Email, c.RegistrationId,
                   z.Zipcode AS Postcode, ci.City, c.Status,
                   (SELECT COUNT(*) FROM ss_pending_changes p
                    WHERE p.candidate_id = c.Id AND p.status = 'pending') AS PendingCount
            FROM Candidates c
            LEFT JOIN Zipcodes z ON z.Id = c.ZipcodeId
            LEFT JOIN Cities ci ON ci.Id = z.CityId
            WHERE (@anyRegion = 0 OR ci.RegionId IN @regions)
              AND (@anyRole = 0 OR EXISTS (SELECT 1 FROM CandidateTags ct
                                           WHERE ct.CandidateId = c.Id AND ct.TagId IN @roles))
              AND (@anyField = 0 OR EXISTS (SELECT 1 FROM CandidateSkills cs
                                            WHERE cs.CandidateId = c.Id AND cs.Skill IN @fields))
              AND (@anyStatus = 0 OR c.Status IN @statuses)
              AND (@pendingOnly = 0 OR EXISTS (SELECT 1 FROM ss_pending_changes p
                                               WHERE p.candidate_id = c.Id AND p.status = 'pending'))
            """,
            new
            {
                anyRegion = filters.Regions.Count > 0,
                regions = filters.Regions,
                anyRole = filters.Roles.Count > 0,
                roles = filters.Roles,
                anyField = filters.Fields.Count > 0,
                fields = filters.Fields,
                anyStatus = filters.Statuses.Count > 0,
                statuses = filters.Statuses,
                pendingOnly = filters.PendingOnly,
            },
            transaction);

        // The search box: matched here, not in SQL. A varying number of words would mean SQL built from text, and
        // SQLite's LIKE and lower() only fold A-Z, so "øster" would miss "Østergaard" (decisions.md, S1).
        var words = filters.Words.Select(word => word.Normalize(NormalizationForm.FormC)).ToList();

        return [.. rows
            .Where(row => words.All(word => Matches(row, word)))
            .OrderBy(row => row.LastName, DanishOrder.Comparer)
            .ThenBy(row => row.FirstName, DanishOrder.Comparer)
            .ThenBy(row => row.Id)
            .Select(row => new ExpertHit(
                row.Id, FullName(row), row.RegistrationId, row.Postcode, row.City, row.Status, row.PendingCount))];
    }

    /// <summary>
    /// A word matches part of a name or of the email, or the start of the postcode. OrdinalIgnoreCase folds Æ/æ, Ø/ø
    /// and Å/å, but never treats "aa" as "å" or "ae" as "æ", as a Danish culture comparison would.
    /// </summary>
    private static bool Matches(HitRow row, string word) =>
        Contains(row.FirstName, word)
        || Contains(row.MiddleName, word)
        || Contains(row.LastName, word)
        || Contains(row.Email, word)
        || (row.Postcode?.StartsWith(word, StringComparison.Ordinal) ?? false);

    private static bool Contains(string? text, string word) =>
        text is not null && text.Normalize(NormalizationForm.FormC).Contains(word, StringComparison.OrdinalIgnoreCase);

    private static string FullName(HitRow row) =>
        string.Join(' ', new[] { row.FirstName, row.MiddleName, row.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private sealed class ListRow
    {
        public string Value { get; init; } = "";

        public string Name { get; init; } = "";
    }

    private sealed class HitRow
    {
        public long Id { get; init; }

        public string FirstName { get; init; } = "";

        public string? MiddleName { get; init; }

        public string LastName { get; init; } = "";

        public string? Email { get; init; }

        public string? RegistrationId { get; init; }

        public string? Postcode { get; init; }

        public string? City { get; init; }

        public int Status { get; init; }

        public int PendingCount { get; init; }
    }
}
