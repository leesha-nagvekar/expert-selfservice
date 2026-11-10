using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ExpertSelfService.Services;

public enum PersonKind
{
    Expert,
    Staff,
}

/// <summary>Who logged in: an expert (Candidates.Id) or a secretariat user (ss_staff_users.id).</summary>
public sealed record Person(PersonKind Kind, long Id);

/// <summary>Person is set when the code was right. LockedFor is set when the email is locked.</summary>
public sealed record CheckResult(Person? Person, TimeSpan? LockedFor);

/// <summary>
/// Story E1: one-time codes by email. SPEC section 4, X1 and X2.
/// Emails reach this class trimmed and in lower case (decisions.md, "Emails are compared without case").
/// </summary>
public sealed class LoginService(
    Database database,
    AppSettings settings,
    TextService text,
    TimeProvider clock,
    ILogger<LoginService> logger)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    public const int MaxFailedTries = 5;

    private const string Purpose = "login";

    private readonly byte[] _key = Encoding.UTF8.GetBytes(settings.CodeKey);

    /// <summary>
    /// Makes a code and "sends" it, but only to an expert who can log in or a staff user.
    /// Returns the time left when the email is locked, otherwise null, whether or not the email is known.
    /// </summary>
    public TimeSpan? RequestCode(string email)
    {
        var now = clock.GetUtcNow();
        string? code = null;

        using var db = database.Open();
        using (var transaction = db.BeginTransaction())
        {
            var lockedFor = LockedFor(db, transaction, email, now);
            if (lockedFor is null && FindPerson(db, transaction, email) is not null)
            {
                code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
                db.Execute(
                    """
                    INSERT INTO ss_login_codes (email, purpose, code_hash, created_at, expires_at)
                    VALUES (@email, @Purpose, @hash, @createdAt, @expiresAt)
                    """,
                    new
                    {
                        email,
                        Purpose,
                        hash = Hash(email, code),
                        createdAt = Database.ToText(now),
                        expiresAt = Database.ToText(now + CodeLifetime),
                    },
                    transaction);
                DevMailbox.Add(
                    db, transaction, email,
                    text["mail.login.subject"],
                    string.Format(CultureInfo.CurrentCulture, text["mail.login.body"], code, CodeLifetime.TotalMinutes),
                    Database.ToText(now));
            }

            transaction.Commit();
            if (lockedFor is not null)
            {
                return lockedFor;
            }
        }

        if (code is not null)
        {
            // CLAUDE.md ground rule 5: codes go to the console and the dev mailbox, never out of the machine.
            logger.LogInformation("Dev mailbox: login code {Code} for {Email}", code, email);
        }

        return null;
    }

    /// <summary>
    /// Checks a code in one write transaction, so guesses sent at the same time
    /// are counted one after the other and can't get past the limit.
    /// </summary>
    public CheckResult CheckCode(string email, string code)
    {
        var now = clock.GetUtcNow();
        using var db = database.Open();
        using var transaction = db.BeginTransaction();

        // Locked: refuse even the right code.
        var lockedFor = LockedFor(db, transaction, email, now);
        if (lockedFor is not null)
        {
            transaction.Commit();
            return new CheckResult(null, lockedFor);
        }

        // Only the newest login code counts, so a new code makes the older ones invalid.
        var row = db.QuerySingleOrDefault<CodeRow>(
            """
            SELECT id AS Id, code_hash AS CodeHash, expires_at AS ExpiresAt, used_at AS UsedAt
            FROM ss_login_codes
            WHERE email = @email AND purpose = @Purpose
            ORDER BY id DESC
            LIMIT 1
            """,
            new { email, Purpose },
            transaction);

        var codeIsRight = row is { UsedAt: null }
            && Database.FromText(row.ExpiresAt) > now
            && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(row.CodeHash), Encoding.ASCII.GetBytes(Hash(email, code)));

        // Looked up again: the person may have been retired since the code was sent.
        var person = codeIsRight ? FindPerson(db, transaction, email) : null;
        if (person is not null)
        {
            db.Execute(
                "UPDATE ss_login_codes SET used_at = @now WHERE id = @Id",
                new { now = Database.ToText(now), row!.Id },
                transaction);
            db.Execute("DELETE FROM ss_login_lockouts WHERE email = @email", new { email }, transaction);
            transaction.Commit();
            return new CheckResult(person, null);
        }

        // A wrong try counts for every email, known or not, so the lock reveals nothing.
        var failedTries = db.ExecuteScalar<int>(
            """
            INSERT INTO ss_login_lockouts (email, failed_count) VALUES (@email, 1)
            ON CONFLICT (email) DO UPDATE SET failed_count = failed_count + 1
            RETURNING failed_count
            """,
            new { email },
            transaction);

        TimeSpan? lockedNow = null;
        if (failedTries >= MaxFailedTries)
        {
            db.Execute(
                "UPDATE ss_login_lockouts SET locked_until = @until WHERE email = @email",
                new { email, until = Database.ToText(now + LockDuration) },
                transaction);
            lockedNow = LockDuration;
        }

        transaction.Commit();
        return new CheckResult(null, lockedNow);
    }

    /// <summary>The time left on the lock, or null. A lock that has run out is removed, which also resets the count.</summary>
    private static TimeSpan? LockedFor(SqliteConnection db, SqliteTransaction transaction, string email, DateTimeOffset now)
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

    /// <summary>An expert who can log in (every status except Retired), else a staff user, else null.</summary>
    private static Person? FindPerson(SqliteConnection db, SqliteTransaction transaction, string email)
    {
        // Candidates is VBA's own table without NOCASE, so both sides are lower-cased.
        var expertId = db.QueryFirstOrDefault<long?>(
            "SELECT Id FROM Candidates WHERE lower(Email) = lower(@email) AND Status <> @retired ORDER BY Id LIMIT 1",
            new { email, retired = ExpertStatus.Retired },
            transaction);
        if (expertId is long expert)
        {
            return new Person(PersonKind.Expert, expert);
        }

        var staffId = db.QueryFirstOrDefault<long?>(
            "SELECT id FROM ss_staff_users WHERE email = @email", new { email }, transaction);
        return staffId is long staff ? new Person(PersonKind.Staff, staff) : null;
    }

    /// <summary>
    /// HMAC-SHA256 with CODE_KEY. A plain hash of a 6-digit code is reversed by trying all million codes.
    /// Purpose and email are part of the input, so a hash only fits one email and one use.
    /// </summary>
    private string Hash(string email, string code) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{Purpose}:{email}:{code}")));

    private sealed class CodeRow
    {
        public long Id { get; init; }

        public string CodeHash { get; init; } = "";

        public string ExpiresAt { get; init; } = "";

        public string? UsedAt { get; init; }
    }
}
