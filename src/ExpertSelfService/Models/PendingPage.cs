using ExpertSelfService.Services;

namespace ExpertSelfService.Models;

/// <summary>One waiting change as S-B shows it. A name change has Old and New; a skills change has Added and Removed.</summary>
public sealed record PendingRowView(
    long Id,
    long ExpertId,
    string ExpertName,
    string? RegistrationId,
    string FieldLabel,
    bool IsSkills,
    string? OldValue,
    string? NewValue,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    string Sent);

/// <summary>
/// The message after a decision, shown once: approved, rejected or already decided. S-B and S-C keep it in TempData
/// under the same two keys, like "Saved" on the expert pages.
/// </summary>
public sealed record DecisionMessage(string Kind, string Text)
{
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string AlreadyDecided = "decided";

    public const string KindKey = "pending-message-kind";
    public const string TextKey = "pending-message-text";
}

/// <summary>
/// The Decision cell for one pending change, shared by S-B and S-C (Views/Shared/_DecisionCell.cshtml): where the two
/// forms post, the hidden fields they send back (S-B's search text), and the reason error for this row, if any.
/// </summary>
public sealed record DecisionCell(
    long ChangeId,
    string ApproveUrl,
    string RejectUrl,
    IReadOnlyList<KeyValuePair<string, string>> Hidden,
    string? Error = null,
    string? Reason = null);

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

    /// <summary>The field's name as the expert's own pages call it (one map, shared with the change log on S-C).</summary>
    public static string FieldLabel(TextService text, string field) => FieldLabels.For(text, field);

    /// <summary>One row as S-B shows it. S-C shows the same rows for one expert.</summary>
    public static PendingRowView Row(TextService text, PendingItem item)
    {
        var isSkills = item.Field == SkillRules.Field;
        var (added, removed) = isSkills ? SkillRules.Diff(item.OldValue, item.NewValue) : ([], []);

        return new PendingRowView(
            item.Id,
            item.ExpertId,
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
