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
        db.Execute(
            """
            INSERT INTO Candidates (Id, FirstName, LastName, Email, Status) VALUES
                (1, 'Anna', 'Demo', @Anna, 1),
                (2, 'Bo', 'Demo', @Bo, -1),
                (3, 'Rita', 'Retired', @Rita, 0);
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
