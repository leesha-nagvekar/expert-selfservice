using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ExpertSelfService.Services;

/// <summary>
/// One-time codes and the lock on wrong tries, shared by login (E1) and email change (E4). SPEC X2 and X6:
/// 6 digits, valid for 10 minutes, used once, only the newest code counts; 5 wrong tries lock the email for 15 minutes.
/// Every method runs in the caller's transaction. Emails arrive trimmed and in lower case.
/// </summary>
public sealed class OneTimeCodes(AppSettings settings)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    public const int MaxFailedTries = 5;

    public const string Login = "login";
    public const string EmailChange = "email_change";

    private readonly byte[] _key = Encoding.UTF8.GetBytes(settings.CodeKey);

    /// <summary>Makes a new code for this email and purpose, stores only its hash, and returns it.</summary>
    public string Issue(SqliteConnection db, SqliteTransaction transaction, string email, string purpose, DateTimeOffset now)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        db.Execute(
            """
            INSERT INTO ss_login_codes (email, purpose, code_hash, created_at, expires_at)
            VALUES (@email, @purpose, @hash, @createdAt, @expiresAt)
            """,
            new
            {
                email,
                purpose,
                hash = Hash(purpose, email, code),
                createdAt = Database.ToText(now),
                expiresAt = Database.ToText(now + CodeLifetime),
            },
            transaction);
        return code;
    }

    /// <summary>
    /// The id of the newest code for this email and purpose when it is unused, not expired and equal to the code
    /// typed; otherwise null. Only the newest code counts, so a new code makes the older ones invalid.
    /// </summary>
    public long? Check(SqliteConnection db, SqliteTransaction transaction, string email, string purpose, string code, DateTimeOffset now)
    {
        var row = db.QuerySingleOrDefault<CodeRow>(
            """
            SELECT id AS Id, code_hash AS CodeHash, expires_at AS ExpiresAt, used_at AS UsedAt
            FROM ss_login_codes
            WHERE email = @email AND purpose = @purpose
            ORDER BY id DESC
            LIMIT 1
            """,
            new { email, purpose },
            transaction);

        var right = row is { UsedAt: null }
            && Database.FromText(row.ExpiresAt) > now
            && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(row.CodeHash), Encoding.ASCII.GetBytes(Hash(purpose, email, code)));
        return right ? row!.Id : null;
    }

    public static void MarkUsed(SqliteConnection db, SqliteTransaction transaction, long codeId, DateTimeOffset now) =>
        db.Execute(
            "UPDATE ss_login_codes SET used_at = @now WHERE id = @codeId",
            new { now = Database.ToText(now), codeId },
            transaction);

    /// <summary>The time left on the lock, or null. A lock that has run out is removed, which also resets the count.</summary>
    public static TimeSpan? LockedFor(SqliteConnection db, SqliteTransaction transaction, string email, DateTimeOffset now)
    {
        var lockedUntil = db.QuerySingleOrDefault<string?>(
            "SELECT locked_until FROM ss_login_lockouts WHERE email = @email", new { email }, transaction);
        if (lockedUntil is null)
        {
            return null;
        }

        var until = Database.FromText(lockedUntil);
        if (until > now)
        {
            return until - now;
        }

        db.Execute("DELETE FROM ss_login_lockouts WHERE email = @email", new { email }, transaction);
        return null;
    }

    /// <summary>Counts one wrong try for this email. Returns the lock time when this try locked it, otherwise null.</summary>
    public static TimeSpan? CountFailure(SqliteConnection db, SqliteTransaction transaction, string email, DateTimeOffset now)
    {
        var failedTries = db.ExecuteScalar<int>(
            """
            INSERT INTO ss_login_lockouts (email, failed_count) VALUES (@email, 1)
            ON CONFLICT (email) DO UPDATE SET failed_count = failed_count + 1
            RETURNING failed_count
            """,
            new { email },
            transaction);
        if (failedTries < MaxFailedTries)
        {
            return null;
        }

        db.Execute(
            "UPDATE ss_login_lockouts SET locked_until = @until WHERE email = @email",
            new { email, until = Database.ToText(now + LockDuration) },
            transaction);
        return LockDuration;
    }

    /// <summary>A right code resets the count.</summary>
    public static void ClearFailures(SqliteConnection db, SqliteTransaction transaction, string email) =>
        db.Execute("DELETE FROM ss_login_lockouts WHERE email = @email", new { email }, transaction);

    /// <summary>
    /// HMAC-SHA256 with CODE_KEY. A plain hash of a 6-digit code is reversed by trying all million codes.
    /// Purpose and email are part of the input, so a hash only fits one email and one use.
    /// </summary>
    private string Hash(string purpose, string email, string code) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{purpose}:{email}:{code}")));

    private sealed class CodeRow
    {
        public long Id { get; init; }

        public string CodeHash { get; init; } = "";

        public string ExpiresAt { get; init; } = "";

        public string? UsedAt { get; init; }
    }
}
