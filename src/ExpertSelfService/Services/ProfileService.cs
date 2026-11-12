using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ExpertSelfService.Services;

/// <summary>The newest ss_pending_changes row for one field, when it is pending or rejected.</summary>
public sealed record PendingState(string Field, string? NewValue, string Status, string? Reason)
{
    public bool IsPending => Status == "pending";
}

/// <summary>Everything screen X4 shows. Read-only: E2 writes nothing (SPEC section 6).</summary>
public sealed record ExpertProfile(
    string FirstName,
    string? MiddleName,
    string LastName,
    string? Street,
    string? Postcode,
    string? City,
    string? PhoneMobile,
    string? PhoneWork,
    string? PhoneHome,
    string? Email,
    string? Company,
    string? Cvr,
    string? Designation,
    string? RegistrationId,
    string? Created,
    int Status,
    string? UntilDate,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Roles,
    IReadOnlyDictionary<string, PendingState> States);

/// <summary>Story E2. Loads one expert's profile by the id from the session, never from the URL (SPEC section 2).</summary>
public sealed class ProfileService(Database database)
{
    /// <summary>
    /// The profile, or null when the expert doesn't exist or is Retired.
    /// Notes, Ratings, Address, Fax, Website and the name-history columns are never selected (SPEC X4).
    /// </summary>
    public ExpertProfile? Load(long expertId)
    {
        using var db = database.Open();
        // Deferred: a read transaction, so the four queries see the same moment without taking the write lock.
        using var transaction = db.BeginTransaction(deferred: true);

        var row = db.QuerySingleOrDefault<ProfileRow>(
            """
            SELECT c.FirstName, c.MiddleName, c.LastName, c.Street, z.Zipcode AS Postcode, ci.City,
                   c.PhoneMobile, c.PhoneWork, c.PhoneHome, c.Email, c.Company, ec.cvr AS Cvr, c.Designation,
                   c.RegistrationId, c.Created, c.Status, c.UntilDate
            FROM Candidates c
            LEFT JOIN Zipcodes z ON z.Id = c.ZipcodeId
            LEFT JOIN Cities ci ON ci.Id = z.CityId
            LEFT JOIN ss_expert_company ec ON ec.candidate_id = c.Id
            WHERE c.Id = @expertId AND c.Status <> @retired
            """,
            new { expertId, retired = ExpertStatus.Retired },
            transaction);
        if (row is null)
        {
            return null;
        }

        var skills = db.Query<string>(
            "SELECT Skill FROM CandidateSkills WHERE CandidateId = @expertId AND Skill IS NOT NULL AND Skill <> ''",
            new { expertId },
            transaction);
        var roles = db.Query<string>(
            """
            SELECT pt.Tag
            FROM CandidateTags ct
            JOIN ProfessionTags pt ON pt.Id = ct.TagId
            WHERE ct.CandidateId = @expertId
            """,
            new { expertId },
            transaction);

        var states = NewestStates(db, transaction, expertId);

        transaction.Commit();

        return new ExpertProfile(
            row.FirstName, row.MiddleName, row.LastName, row.Street, row.Postcode, row.City,
            row.PhoneMobile, row.PhoneWork, row.PhoneHome, row.Email, row.Company, row.Cvr, row.Designation,
            row.RegistrationId, row.Created, row.Status, row.UntilDate,
            SortedForLanguage(skills),
            SortedForLanguage(roles),
            states);
    }

    /// <summary>
    /// The state of each field that is pending or rejected. Only the newest row per field counts: a rejection shows
    /// until a new change to that field, and an approved change shows nothing (SPEC section 3). X4 and X7 both use it.
    /// </summary>
    public static Dictionary<string, PendingState> NewestStates(SqliteConnection db, SqliteTransaction transaction, long expertId) =>
        db.Query<PendingState>(
            """
            SELECT p.field AS Field, p.new_value AS NewValue, p.status AS Status, p.reason AS Reason
            FROM ss_pending_changes p
            WHERE p.candidate_id = @expertId
              AND p.status IN ('pending', 'rejected')
              AND p.id = (SELECT MAX(newest.id) FROM ss_pending_changes newest
                          WHERE newest.candidate_id = @expertId AND newest.field = p.field)
            """,
            new { expertId },
            transaction).ToDictionary(state => state.Field);

    // Sorted in C#, not SQL: SQLite sorts by bytes, which puts Å before Æ. The language's own order is used.
    private static List<string> SortedForLanguage(IEnumerable<string> names) =>
        [.. names.Order(StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))];

    private sealed class ProfileRow
    {
        public string FirstName { get; init; } = "";

        public string? MiddleName { get; init; }

        public string LastName { get; init; } = "";

        public string? Street { get; init; }

        public string? Postcode { get; init; }

        public string? City { get; init; }

        public string? PhoneMobile { get; init; }

        public string? PhoneWork { get; init; }

        public string? PhoneHome { get; init; }

        public string? Email { get; init; }

        public string? Company { get; init; }

        public string? Cvr { get; init; }

        public string? Designation { get; init; }

        public string? RegistrationId { get; init; }

        public string? Created { get; init; }

        public int Status { get; init; }

        public string? UntilDate { get; init; }
    }
}
