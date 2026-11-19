using ExpertSelfService.Services;

namespace ExpertSelfService.Models;

/// <summary>One waiting change as S-B shows it. A name change has Old and New; a skills change has Added and Removed.</summary>
public sealed record PendingRowView(
    long Id,
    string ExpertName,
    string? RegistrationId,
    string FieldLabel,
    bool IsSkills,
    string? OldValue,
    string? NewValue,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    string Sent);

/// <summary>The message after a decision, shown once: approved, rejected or already decided.</summary>
public sealed record DecisionMessage(string Kind, string Text)
{
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string AlreadyDecided = "decided";
}

/// <summary>
/// Screen S-B, story S2. Rows is null when the search text is too long. A failed reject keeps its row's reason box
/// open with the error and the text that was sent.
/// </summary>
public sealed record PendingPage(
    string? Query,
    string? QueryError,
    IReadOnlyList<PendingRowView>? Rows,
    DecisionMessage? Message,
    long? ErrorRowId = null,
    string? ReasonError = null,
    string? Reason = null)
{
    public static PendingPage Build(TextService text, string? query, string? queryError, IEnumerable<PendingItem>? items, DecisionMessage? message) =>
        new(query, queryError, items?.Select(item => Row(text, item)).ToList(), message);

    /// <summary>The field's name as the expert's own pages call it.</summary>
    public static string FieldLabel(TextService text, string field) => field switch
    {
        NameRules.FirstName => text["profile.first_name"],
        NameRules.MiddleName => text["profile.middle_name"],
        NameRules.LastName => text["profile.last_name"],
        SkillRules.Field => text["profile.section.skills"],
        _ => field,
    };

    private static PendingRowView Row(TextService text, PendingItem item)
    {
        var isSkills = item.Field == SkillRules.Field;
        var (added, removed) = isSkills ? SkillRules.Diff(item.OldValue, item.NewValue) : ([], []);

        return new PendingRowView(
            item.Id,
            item.ExpertName,
            item.RegistrationId,
            FieldLabel(text, item.Field),
            isSkills,
            item.OldValue,
            item.NewValue,
            added,
            removed,
            LocalTime.Format(item.CreatedAt));
    }
}
