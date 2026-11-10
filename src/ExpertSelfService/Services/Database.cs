using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ExpertSelfService.Services;

/// <summary>Connections to the SQLite test database. Every query uses Dapper parameters (@name).</summary>
public sealed class Database(AppSettings settings)
{
    /// <summary>How every ss_ table stores a time: UTC, as in db/schema_sqlite.sql.</summary>
    public const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = settings.DatabasePath,
        // ReadWrite, not ReadWriteCreate: a wrong path gives an error, never a new empty database.
        Mode = SqliteOpenMode.ReadWrite,
        // SQLite turns foreign keys on per connection, not per file.
        ForeignKeys = true,
    }.ToString();

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public static string ToText(DateTimeOffset time) =>
        time.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture);

    public static DateTimeOffset FromText(string text) =>
        DateTimeOffset.ParseExact(
            text, TimeFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
