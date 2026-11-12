using System.Globalization;
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
/// Story E1: one-time codes by email. SPEC section 4, X1 and X2. The codes and the lock are in OneTimeCodes.
/// Emails reach this class trimmed and in lower case (decisions.md, "Emails are compared without case").
/// </summary>
public sealed class LoginService(
    Database database,
    OneTimeCodes codes,
    TextService text,
    TimeProvider clock,
    ILogger<LoginService> logger)
{
    public static readonly TimeSpan CodeLifetime = OneTimeCodes.CodeLifetime;

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
            var lockedFor = OneTimeCodes.LockedFor(db, transaction, email, now);
            if (lockedFor is null && FindPerson(db, transaction, email) is not null)
            {
                code = codes.Issue(db, transaction, email, OneTimeCodes.Login, now);
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
        var lockedFor = OneTimeCodes.LockedFor(db, transaction, email, now);
        if (lockedFor is not null)
        {
            transaction.Commit();
            return new CheckResult(null, lockedFor);
        }

        var codeId = codes.Check(db, transaction, email, OneTimeCodes.Login, code, now);

        // Looked up again: the person may have been retired since the code was sent.
        var person = codeId is not null ? FindPerson(db, transaction, email) : null;
        if (person is not null)
        {
            OneTimeCodes.MarkUsed(db, transaction, codeId!.Value, now);
            OneTimeCodes.ClearFailures(db, transaction, email);
            transaction.Commit();
            return new CheckResult(person, null);
        }

        // A wrong try counts for every email, known or not, so the lock reveals nothing.
        var lockedNow = OneTimeCodes.CountFailure(db, transaction, email, now);
        transaction.Commit();
        return new CheckResult(null, lockedNow);
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
}
