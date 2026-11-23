using Dapper;

namespace ExpertSelfService.Services;

/// <summary>
/// Story S3, screen S-C: one expert's details for the secretariat. Read-only. Unlike the expert's own profile
/// (<see cref="ProfileService.Load"/>, left as it is), a Retired expert is found too: the secretariat owns the record.
/// The id comes from the URL, so only staff may call it (the controller's policy).
/// </summary>
public sealed class StaffExpertService(Database database)
{
    /// <summary>
    /// The expert, or null when there is no such id.
    /// Notes, Ratings, Address, Fax, Website and the name-history columns are never selected (SPEC X4).
    /// </summary>
    public ExpertProfile? Load(long expertId)
    {
        using var db = database.Open();
        // Deferred: a read transaction, so the queries see the same moment without taking the write lock.
        using var transaction = db.BeginTransaction(deferred: true);

        var row = db.QuerySingleOrDefault<Row>(
            """
            SELECT c.FirstName, c.MiddleName, c.LastName, c.Street, z.Zipcode AS Postcode, ci.City,
                   c.PhoneMobile, c.PhoneWork, c.PhoneHome, c.Email, c.Company, ec.cvr AS Cvr, c.Designation,
                   c.RegistrationId, c.Created, c.Status, c.UntilDate
            FROM Candidates c
            LEFT JOIN Zipcodes z ON z.Id = c.ZipcodeId
            LEFT JOIN Cities ci ON ci.Id = z.CityId
            LEFT JOIN ss_expert_company ec ON ec.candidate_id = c.Id
            WHERE c.Id = @expertId
            """,
            new { expertId },
            transaction);
        if (row is null)
        {
            return null;
        }

        var skills = db.Query<string>(
            "SELECT Skill FROM CandidateSkills WHERE CandidateId = @expertId AND Skill IS NOT NULL AND Skill <> ''",
            new { expertId },
            transaction);
        var roles = ProfileService.Roles(db, transaction, expertId);
        var states = ProfileService.NewestStates(db, transaction, expertId);

        transaction.Commit();

        // Fields and roles in one fixed order, whatever the page's language, as on S-A, S-B and in the change log
        // (review finding #4). The expert's own pages keep their language's order (E2), so the roles from
        // ProfileService.Roles are sorted again here.
        return new ExpertProfile(
            row.FirstName, row.MiddleName, row.LastName, row.Street, row.Postcode, row.City,
            row.PhoneMobile, row.PhoneWork, row.PhoneHome, row.Email, row.Company, row.Cvr, row.Designation,
            row.RegistrationId, row.Created, row.Status, row.UntilDate,
            SkillRules.Sorted(skills),
            [.. roles.Order(DanishOrder.Comparer)],
            states);
    }

    private sealed class Row
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
