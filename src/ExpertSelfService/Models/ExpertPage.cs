using System.Globalization;
using ExpertSelfService.Services;

namespace ExpertSelfService.Models;

/// <summary>One change-log row as S-C shows it: local time, who, the field's label, old and new value.</summary>
public sealed record LogRowView(string When, string Who, bool ByStaff, string Field, string? OldValue, string? NewValue);

/// <summary>
/// Screen S-C, story S3: one expert for the secretariat. The details are read-only; the pending changes are the same
/// rows as on S-B, with the same Decision cell; the change log is one page of 10, newest first, with the same pager as
/// S-A. After a decision the one-time message shows; a failed reject keeps its row's reason box open with the error.
/// </summary>
public sealed record ExpertPage(
    long ExpertId,
    ExpertProfile Profile,
    string Name,
    string? Created,
    string StatusText,
    string StatusIcon,
    string StatusClass,
    string? StatusUntil,
    IReadOnlyList<PendingRowView> Pending,
    IReadOnlyList<LogRowView> Log,
    Pager Pager,
    string Back = BackLink.Search,
    DecisionMessage? Message = null,
    long? ErrorRowId = null,
    string? ReasonError = null,
    string? Reason = null)
{
    public static string Url(long expertId) => $"/staff/experts/{expertId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Where S-C's Decision cell posts: always under this expert, so the change is checked against it.</summary>
    public static string DecisionUrl(long expertId, long changeId, string action) =>
        $"{Url(expertId)}/changes/{changeId.ToString(CultureInfo.InvariantCulture)}/{action}";

    /// <summary>"Back to pending approvals" when the link goes to S-B, else "Back to search".</summary>
    public string BackTextKey => BackLink.IsPending(Back) ? "expert.back.pending" : "expert.back.search";

    /// <summary>The Decision cell for one pending row. The forms send the back link along, unless it is plain /staff.</summary>
    public DecisionCell Cell(PendingRowView row) =>
        new(row.Id, DecisionUrl(ExpertId, row.Id, "approve"), DecisionUrl(ExpertId, row.Id, "reject"),
            Back == BackLink.Search ? [] : [KeyValuePair.Create("back", Back)],
            ErrorRowId == row.Id ? ReasonError : null, ErrorRowId == row.Id ? Reason : null);

    /// <summary>
    /// The page's URL for a log page, keeping the back link (page 1 has no ?page, as on S-A; plain /staff has no ?back).
    /// <paramref name="back"/> must already be safe (<see cref="BackLink.Safe"/>).
    /// </summary>
    public static string PageUrl(long expertId, int page, string back = BackLink.Search)
    {
        string?[] parts = [page > 1 ? $"page={page.ToString(CultureInfo.InvariantCulture)}" : null, BackLink.Query(back)];
        var query = string.Join("&", parts.Where(part => part is not null));
        return query.Length == 0 ? Url(expertId) : $"{Url(expertId)}?{query}";
    }

    /// <summary>
    /// A name link from a list (S-A or S-B) to the expert page, carrying the list's URL so S-C can link back to it as
    /// it was. A list URL the back link would not accept (for example one over 2000 characters) is simply left out.
    /// </summary>
    public static string LinkFrom(long expertId, string listUrl) => PageUrl(expertId, 1, BackLink.Safe(listUrl));

    public static ExpertPage Build(
        TextService text,
        long expertId,
        ExpertProfile profile,
        IEnumerable<PendingItem> pending,
        ChangeLogPage log,
        int page,
        string back = BackLink.Search)
    {
        var until = profile.Status == ExpertStatus.Standby ? LocalDate.Format(profile.UntilDate) : null;
        return new ExpertPage(
            expertId,
            profile,
            ExpertName.Full(profile.FirstName, profile.MiddleName, profile.LastName),
            LocalDate.Format(profile.Created),
            ExpertStatus.Word(profile.Status, text),
            ExpertStatus.Icon(profile.Status),
            ExpertStatus.CssClass(profile.Status),
            until is null ? null : string.Format(CultureInfo.CurrentCulture, text["profile.until"], until),
            [.. pending.Select(item => PendingPage.Row(text, item))],
            [.. log.Entries.Select(entry => LogRow(text, entry))],
            Pager.Build(page, log.Total, number => PageUrl(expertId, number, back), ChangeLogService.PageSize),
            back);
    }

    private static LogRowView LogRow(TextService text, ChangeLogEntry entry)
    {
        var byStaff = entry.ActorType == ExpertChanges.ByStaff;
        // A staff user that no longer exists still shows who it was, by id.
        var who = byStaff
            ? string.Format(CultureInfo.CurrentCulture, text["expert.log.by_staff"], entry.StaffName ?? $"#{entry.ActorId}")
            : text["expert.log.by_expert"];
        return new LogRowView(
            LocalTime.Format(entry.CreatedAt), who, byStaff, FieldLabels.For(text, entry.Field), entry.OldValue, entry.NewValue);
    }
}
