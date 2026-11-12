using System.Globalization;
using Dapper;

namespace ExpertSelfService.Services;

/// <summary>The outcome of asking for a change: an i18n error key, a lock, or (both null) the code was sent.</summary>
public sealed record EmailRequestResult(string? ErrorKey, TimeSpan? LockedFor);

public enum EmailConfirmOutcome
{
    NoRequest,
    Locked,
    Wrong,
    InUse,
    Confirmed,
}

/// <summary>The outcome of typing the code. LockedFor is set when the new email is (or just became) locked.</summary>
public sealed record EmailConfirmResult(EmailConfirmOutcome Outcome, TimeSpan? LockedFor = null);

/// <summary>
/// Story E4, screen X6. A code goes to the new address; Candidates.Email changes only when it is confirmed,
/// so the old email keeps working until then. Emails arrive trimmed and in lower case.
/// </summary>
public sealed class EmailChangeService(
    Database database,
    OneTimeCodes codes,
    ExpertChanges changes,
    TextService text,
    TimeProvider clock,
    ILogger<EmailChangeService> logger)
{
    /// <summary>The C# copy of rules.MAX_LENGTHS["Email"] and rules.EMAIL_DOMAIN; EmailChangeTests reads rules.py.</summary>
    public const int EmailMax = 50;
    public const string Domain = "@example.com";

    /// <summary>The expert's current email, or null when the expert is gone or Retired.</summary>
    public string? CurrentEmail(long expertId)
    {
        using var db = database.Open();
        return db.QuerySingleOrDefault<string?>(
            "SELECT Email FROM Candidates WHERE Id = @expertId AND Status <> @retired",
            new { expertId, retired = ExpertStatus.Retired });
    }

    /// <summary>The address of this expert's newest request that is neither confirmed nor cancelled, or null.</summary>
    public string? OpenRequest(long expertId)
    {
        using var db = database.Open();
        return OpenRequest(db, null, expertId)?.NewEmail;
    }

    /// <summary>
    /// Checks the new email and, when it passes, cancels older requests, stores this one and sends a code to the
    /// new address. Whether another person has the address is NOT checked here: that would let any logged-in
    /// expert find out who is registered. It is checked after the right code (decisions.md, E4).
    /// </summary>
    public EmailRequestResult Request(long expertId, string newEmail)
    {
        if (newEmail.Length == 0)
        {
            return new("email.error.required", null);
        }

        if (newEmail.Length > EmailMax)
        {
            return new("email.error.too_long", null);
        }

        if (!EmailFormat.IsValid(newEmail))
        {
            return new("login.email_invalid", null);
        }

        if (!newEmail.EndsWith(Domain, StringComparison.Ordinal))
        {
            return new("email.error.domain", null);
        }

        var now = clock.GetUtcNow();
        string code;
        using (var db = database.Open())
        using (var transaction = db.BeginTransaction())
        {
            var current = db.QuerySingleOrDefault<string?>(
                "SELECT Email FROM Candidates WHERE Id = @expertId AND Status <> @retired",
                new { expertId, retired = ExpertStatus.Retired },
                transaction);
            if (current is not null && string.Equals(current, newEmail, StringComparison.OrdinalIgnoreCase))
            {
                return new("email.error.same", null);
            }

            if (OneTimeCodes.LockedFor(db, transaction, newEmail, now) is TimeSpan lockedFor)
            {
                transaction.Commit(); // keeps the removal of a lock that has run out
                return new(null, lockedFor);
            }

            var nowText = Database.ToText(now);
            db.Execute(
                """
                UPDATE ss_email_changes SET cancelled_at = @nowText
                WHERE candidate_id = @expertId AND confirmed_at IS NULL AND cancelled_at IS NULL
                """,
                new { nowText, expertId },
                transaction);
            db.Execute(
                "INSERT INTO ss_email_changes (candidate_id, new_email, created_at) VALUES (@expertId, @newEmail, @nowText)",
                new { expertId, newEmail, nowText },
                transaction);
            code = codes.Issue(db, transaction, newEmail, OneTimeCodes.EmailChange, now);
            DevMailbox.Add(
                db, transaction, newEmail,
                text["mail.email_change.subject"],
                string.Format(CultureInfo.CurrentCulture, text["mail.login.body"], code, OneTimeCodes.CodeLifetime.TotalMinutes),
                nowText);
            transaction.Commit();
        }

        // CLAUDE.md ground rule 5: codes go to the console and the dev mailbox, never out of the machine.
        logger.LogInformation("Dev mailbox: email change code {Code} for {Email}", code, newEmail);
        return new(null, null);
    }

    /// <summary>
    /// Checks the code in one write transaction. Right code and the address still free: Candidates.Email is updated,
    /// and ExpertChanges sets LastUpdated and writes the change log, all together.
    /// </summary>
    public EmailConfirmResult Confirm(long expertId, string code)
    {
        var now = clock.GetUtcNow();
        using var db = database.Open();
        using var transaction = db.BeginTransaction();

        var request = OpenRequest(db, transaction, expertId);
        if (request is null)
        {
            return new(EmailConfirmOutcome.NoRequest);
        }

        // Locked: refuse even the right code.
        if (OneTimeCodes.LockedFor(db, transaction, request.NewEmail, now) is TimeSpan lockedFor)
        {
            transaction.Commit();
            return new(EmailConfirmOutcome.Locked, lockedFor);
        }

        var codeId = codes.Check(db, transaction, request.NewEmail, OneTimeCodes.EmailChange, code, now);
        if (codeId is null)
        {
            // Wrong tries count against the new email, never the expert's own login email.
            var lockedNow = OneTimeCodes.CountFailure(db, transaction, request.NewEmail, now);
            transaction.Commit();
            return lockedNow is null
                ? new(EmailConfirmOutcome.Wrong)
                : new(EmailConfirmOutcome.Locked, lockedNow);
        }

        OneTimeCodes.MarkUsed(db, transaction, codeId.Value, now);
        OneTimeCodes.ClearFailures(db, transaction, request.NewEmail);
        var nowText = Database.ToText(now);

        // Checked here, inside the transaction, so two experts can never end up with the same email.
        // Any Candidates row counts, Retired too, and any staff user.
        var inUse = db.ExecuteScalar<long>(
            """
            SELECT (SELECT COUNT(*) FROM Candidates WHERE lower(Email) = lower(@newEmail) AND Id <> @expertId)
                 + (SELECT COUNT(*) FROM ss_staff_users WHERE email = @newEmail)
            """,
            new { newEmail = request.NewEmail, expertId },
            transaction) > 0;
        if (inUse)
        {
            db.Execute(
                "UPDATE ss_email_changes SET cancelled_at = @nowText WHERE id = @Id",
                new { nowText, request.Id },
                transaction);
            transaction.Commit();
            return new(EmailConfirmOutcome.InUse);
        }

        var oldEmail = db.QuerySingle<string?>(
            "SELECT Email FROM Candidates WHERE Id = @expertId", new { expertId }, transaction);
        db.Execute(
            "UPDATE Candidates SET Email = @newEmail WHERE Id = @expertId",
            new { newEmail = request.NewEmail, expertId },
            transaction);
        changes.Record(
            db, transaction, expertId, ExpertChanges.ByExpert, expertId,
            [new FieldChange("Email", oldEmail, request.NewEmail)]);
        db.Execute(
            "UPDATE ss_email_changes SET confirmed_at = @nowText WHERE id = @Id",
            new { nowText, request.Id },
            transaction);

        transaction.Commit();
        return new(EmailConfirmOutcome.Confirmed);
    }

    private sealed record RequestRow(long Id, string NewEmail);

    private static RequestRow? OpenRequest(Microsoft.Data.Sqlite.SqliteConnection db, Microsoft.Data.Sqlite.SqliteTransaction? transaction, long expertId) =>
        db.QueryFirstOrDefault<RequestRow>(
            """
            SELECT id AS Id, new_email AS NewEmail FROM ss_email_changes
            WHERE candidate_id = @expertId AND confirmed_at IS NULL AND cancelled_at IS NULL
            ORDER BY id DESC
            LIMIT 1
            """,
            new { expertId },
            transaction);
}
