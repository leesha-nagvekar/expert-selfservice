using Dapper;
using ExpertSelfService.Models;
using Microsoft.Data.Sqlite;

namespace ExpertSelfService.Services;

public enum SendResult
{
    Sent,
    NothingChanged,
    ExpertNotFound,
}

/// <summary>
/// Story E5, screen X7: the name goes to approval (SPEC section 3). One ss_pending_changes row per changed field;
/// Candidates, LastUpdated and ss_change_log are left alone until S2 decides (SPEC section 6).
/// </summary>
public sealed class NameService(Database database, TimeProvider clock)
{
    private sealed record CurrentRow(string FirstName, string? MiddleName, string LastName);

    /// <summary>The form with the current name and the state of each field, or null when the expert is gone or Retired.</summary>
    public NamePage? Load(long expertId)
    {
        using var db = database.Open();
        // Deferred: a read transaction, so the name and the states are from the same moment.
        using var transaction = db.BeginTransaction(deferred: true);
        var current = Current(db, transaction, expertId);
        if (current is null)
        {
            return null;
        }

        var states = ProfileService.NewestStates(db, transaction, expertId);
        transaction.Commit();

        var form = NameForm.From(current.FirstName, current.MiddleName, current.LastName);
        return new NamePage(form, form, states, new Dictionary<string, string>());
    }

    /// <summary>
    /// The i18n key of the error for each wrong field. Every field is checked (as E3), except a locked one:
    /// its value is ignored anyway, so it is neither checked nor written.
    /// </summary>
    public static Dictionary<string, string> Validate(NameForm form, Func<string, bool> isLocked)
    {
        var errors = new Dictionary<string, string>();
        foreach (var field in NameRules.Fields.Where(field => !isLocked(field)))
        {
            var value = form.Value(field).Trim();
            if (value.Length == 0)
            {
                if (field == NameRules.FirstName)
                {
                    errors[field] = "name.error.first_required";
                }
                else if (field == NameRules.LastName)
                {
                    errors[field] = "name.error.last_required";
                }
            }
            else if (TextRules.HasControlCharacter(value))
            {
                errors[field] = "name.error.control";
            }
            else if (value.Length > NameRules.Max)
            {
                errors[field] = "name.error.too_long";
            }
        }

        return errors;
    }

    /// <summary>
    /// Writes one pending row per changed field of a valid form. A field with a pending change is skipped whatever
    /// the form says: it is read again here, inside the write transaction, so a second tab or a hand-made post
    /// can never give one field two pending changes.
    /// </summary>
    public SendResult Send(long expertId, NameForm form)
    {
        using var db = database.Open();
        // Not deferred: the write lock is taken at the start, so two sends at once run one after the other.
        using var transaction = db.BeginTransaction();

        var current = Current(db, transaction, expertId);
        if (current is null)
        {
            return SendResult.ExpertNotFound;
        }

        var states = ProfileService.NewestStates(db, transaction, expertId);
        var currentValues = new Dictionary<string, string?>
        {
            [NameRules.FirstName] = current.FirstName,
            [NameRules.MiddleName] = current.MiddleName,
            [NameRules.LastName] = current.LastName,
        };

        var createdAt = Database.ToText(clock.GetUtcNow());
        var sent = 0;
        foreach (var field in NameRules.Fields)
        {
            if (states.GetValueOrDefault(field)?.IsPending == true)
            {
                continue;
            }

            // Exact comparison: "anna" -> "Anna" is a correction, so a change. Spaces around don't count.
            var oldValue = TextRules.Clean(currentValues[field]);
            var newValue = TextRules.Clean(form.Value(field));
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                continue;
            }

            db.Execute(
                """
                INSERT INTO ss_pending_changes (candidate_id, field, old_value, new_value, status, created_at)
                VALUES (@expertId, @field, @oldValue, @newValue, 'pending', @createdAt)
                """,
                new { expertId, field, oldValue, newValue, createdAt },
                transaction);
            sent++;
        }

        transaction.Commit();
        return sent == 0 ? SendResult.NothingChanged : SendResult.Sent;
    }

    private static CurrentRow? Current(SqliteConnection db, SqliteTransaction transaction, long expertId) =>
        db.QuerySingleOrDefault<CurrentRow>(
            "SELECT FirstName, MiddleName, LastName FROM Candidates WHERE Id = @expertId AND Status <> @retired",
            new { expertId, retired = ExpertStatus.Retired },
            transaction);
}
