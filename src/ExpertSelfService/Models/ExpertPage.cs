using System.Globalization;
using ExpertSelfService.Services;

namespace ExpertSelfService.Models;

/// <summary>One change-log row as S-C shows it: local time, who, the field's label, old and new value.</summary>
public sealed record LogRowView(string When, string Who, bool ByStaff, string Field, string? OldValue, string? NewValue);

/// <summary>
/// Screen S-C, story S3: one expert for the secretariat. The details are read-only; the pending changes are the same
/// rows as on S-B; the change log is one page of 10, newest first, with the same pager as S-A.
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
    Pager Pager)
{
    public static string Url(long expertId) => $"/staff/experts/{expertId}";

    /// <summary>The page's URL for a log page (page 1 has no ?page, as on S-A).</summary>
    public static string PageUrl(long expertId, int page) =>
        page > 1 ? $"{Url(expertId)}?page={page.ToString(CultureInfo.InvariantCulture)}" : Url(expertId);

    public static ExpertPage Build(
        TextService text, long expertId, ExpertProfile profile, IEnumerable<PendingItem> pending, ChangeLogPage log, int page)
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
            Pager.Build(page, log.Total, number => PageUrl(expertId, number), ChangeLogService.PageSize));
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
