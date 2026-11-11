using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertSelfService.Tests;

/// <summary>A clock the tests can move forward.</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// Runs the app in memory on its own temp database, built from db/schema_sqlite.sql.
/// Tests never touch data/expert_register.db and need no .env file.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    // Made up for the tests only. Never used by the real app.
    public const string CodeKey = "test-only-code-key-0123456789abcdefghijklmnop";

    // Fake people, all @example.com.
    public const string Anna = "anna.demo@example.com";          // Active (1)
    public const string Bo = "bo.demo@example.com";              // Standby (-1), can log in
    public const string Rita = "rita.retired@example.com";       // Retired (0), can't log in
    public const string Sofie = "sofie.staff@example.com";       // secretariat
    public const string Nobody = "nobody@example.com";           // not registered

    private readonly string _folder;
    private readonly string _environment;
    private readonly string? _codeKey;

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero));

    public string DatabasePath { get; }

    public AppFactory()
        : this("Development", CodeKey, createDatabase: true)
    {
    }

    private AppFactory(string environment, string? codeKey, bool createDatabase)
    {
        _environment = environment;
        _codeKey = codeKey;
        _folder = Path.Combine(Path.GetTempPath(), "expert-self-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        DatabasePath = Path.Combine(_folder, "test.db");
        if (createDatabase)
        {
            CreateDatabase();
        }
    }

    public static AppFactory With(string environment = "Development", string? codeKey = CodeKey, bool createDatabase = true) =>
        new(environment, codeKey, createDatabase);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        // Added last, so these win over a real .env on the developer's machine.
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_PATH"] = DatabasePath,
            ["CODE_KEY"] = _codeKey,
        }));
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath};Foreign Keys=True");
        connection.Open();
        return connection;
    }

    /// <summary>The 6-digit code in the newest dev mailbox message to this email.</summary>
    public string LatestCode(string email)
    {
        using var db = Open();
        var body = db.QuerySingle<string>(
            "SELECT body FROM ss_dev_mailbox WHERE to_email = @email ORDER BY id DESC LIMIT 1",
            new { email = email.ToLowerInvariant() });
        return Regex.Match(body, @"\b[0-9]{6}\b").Value;
    }

    public int Count(string sql, object? parameters = null)
    {
        using var db = Open();
        return db.ExecuteScalar<int>(sql, parameters);
    }

    private void CreateDatabase()
    {
        using var db = Open();
        db.Execute(RepoFiles.Read("db", "schema_sqlite.sql"));
        // Fake details in the style of seed.py's demo experts. Anna has everything,
        // Bo is on Standby with no phone and no company. Skills are inserted unsorted.
        db.Execute(
            """
            INSERT INTO Regions (Id, RegionName) VALUES (2, 'Norge'), (4, 'Region Hovedstaden'), (6, 'Region Syddanmark');
            INSERT INTO Cities (Id, City, RegionId, ZipCode) VALUES
                (2001, 'København Ø', 4, '2100'),
                (2002, 'Odense C', 6, '5000'),
                (2003, 'Aarhus C', 4, '8000'),
                (50, 'Oslo', 2, '0150');
            INSERT INTO Zipcodes (Id, CityId, Zipcode) VALUES
                (2001, 2001, '2100'), (2002, 2002, '5000'), (2003, 2003, '8000'),
                (50, 50, '0150'); -- 4 digits, but Norwegian: not a Danish postcode (rules.DANISH_REGION_IDS)
            INSERT INTO ProfessionTags (Id, Tag) VALUES (35, 'Skønsmand'), (36, 'Sagkyndig');

            INSERT INTO Candidates
                (Id, FirstName, LastName, Email, Status, OldStatus, UntilDate, RegistrationId, Created,
                 Street, ZipcodeId, PhoneMobile, PhoneWork, Company, Designation) VALUES
                (1, 'Anna', 'Demo', @Anna, 1, NULL, NULL, '900001', '2015-03-02',
                 'Østerbrogade 64', 2001, '+45 20 11 22 33', '33 44 55 66', 'Holm Rådgivning ApS', 'Rådgivende ingeniør'),
                (2, 'Bo', 'Demo', @Bo, -1, 1, '2027-03-31', '900002', '2019-06-17',
                 'Vestergade 8', 2002, NULL, NULL, NULL, NULL),
                (3, 'Rita', 'Retired', @Rita, 0, NULL, NULL, '900003', '2010-01-04',
                 NULL, NULL, NULL, NULL, NULL, NULL);
            INSERT INTO ss_expert_company (candidate_id, cvr) VALUES (1, '12345674');
            INSERT INTO CandidateSkills (CandidateId, Skill) VALUES
                (1, 'Varmepumpe'), (1, 'EL'), (1, 'Maskinanlæg'), (2, 'Fiber');
            INSERT INTO CandidateTags (CandidateId, TagId) VALUES (1, 35), (2, 36);

            INSERT INTO ss_staff_users (id, name, email, created_at) VALUES
                (1, 'Sofie Staff', @Sofie, '2026-10-01T00:00:00Z');
            """,
            new { Anna, Bo, Rita, Sofie });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Pooled connections keep the file open on Windows.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless.
        }
    }
}
