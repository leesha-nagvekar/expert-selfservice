using Dapper;
using Microsoft.Data.Sqlite;

namespace ExpertSelfService.Services;

public sealed class MailboxMessage
{
    public string ToEmail { get; init; } = "";

    public string Subject { get; init; } = "";

    public string Body { get; init; } = "";

    public string CreatedAt { get; init; } = "";
}

/// <summary>
/// Screen X3. The app never sends email (CLAUDE.md ground rule 5): a message is only
/// written to ss_dev_mailbox, and the caller prints the code to the console.
/// </summary>
public sealed class DevMailbox(Database database)
{
    public static void Add(SqliteConnection db, SqliteTransaction transaction, string to, string subject, string body, string createdAt) =>
        db.Execute(
            "INSERT INTO ss_dev_mailbox (to_email, subject, body, created_at) VALUES (@to, @subject, @body, @createdAt)",
            new { to, subject, body, createdAt },
            transaction);

    public IReadOnlyList<MailboxMessage> All()
    {
        using var db = database.Open();
        return db.Query<MailboxMessage>(
            """
            SELECT to_email AS ToEmail, subject AS Subject, body AS Body, created_at AS CreatedAt
            FROM ss_dev_mailbox
            ORDER BY id DESC
            """).AsList();
    }
}
