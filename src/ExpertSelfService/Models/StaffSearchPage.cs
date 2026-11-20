using System.Globalization;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Http.Extensions;

namespace ExpertSelfService.Models;

/// <summary>One tick box in a sidebar group.</summary>
public sealed record FilterOption(string Value, string Label, bool IsTicked);

/// <summary>
/// One sidebar group of tick boxes (SPEC S-A). It folds open and closed, and starts open when something in it is
/// ticked. A group with more than <see cref="ScrollAfter"/> values lists the ticked ones first in a scroll box 8 rows
/// high, with a Find box that narrows the list.
/// </summary>
public sealed record FilterGroup(string Name, string Legend, IReadOnlyList<FilterOption> Options, string? FindLabel)
{
    public const int ScrollAfter = 8;

    public int TickedCount => Options.Count(option => option.IsTicked);

    public bool IsOpen => TickedCount > 0;

    public bool HasScroll => FindLabel is not null;

    /// <param name="options">Every value, in the order the group lists them.</param>
    public static FilterGroup Build(
        string name, string legend, string findLabel, IEnumerable<ListValue> options, IReadOnlyCollection<string> ticked)
    {
        var all = options.Select(option => new FilterOption(option.Value, option.Name, ticked.Contains(option.Value))).ToList();
        if (all.Count <= ScrollAfter)
        {
            return new FilterGroup(name, legend, all, null);
        }

        // Ticked first, each part keeping the list's order.
        var ordered = all.Where(option => option.IsTicked).Concat(all.Where(option => !option.IsTicked)).ToList();
        return new FilterGroup(name, legend, ordered, findLabel);
    }
}

/// <summary>One result row. The name links to the expert page (S-C).</summary>
public sealed record SearchRow(
    long ExpertId,
    string Name,
    string? RegistrationId,
    string? PostcodeCity,
    string StatusText,
    string StatusIcon,
    string StatusClass,
    int PendingCount);

/// <summary>One page link. A null Number is a gap ("…") between page numbers.</summary>
public sealed record PageLink(int? Number, string Url, bool IsCurrent);

/// <summary>The page links under the results: "Showing 1–25 of 100", Previous, the numbers and Next.</summary>
public sealed record Pager(
    int Page, int PageCount, int First, int Last, int Total, string? PreviousUrl, string? NextUrl, IReadOnlyList<PageLink> Links)
{
    /// <summary>Up to this many pages, every number is shown. Above it: first, last, the current ±1 and gaps.</summary>
    public const int ShowAllUpTo = 7;

    /// <param name="url">The URL for a page number, with the search and every tick kept.</param>
    /// <param name="pageSize">Rows per page: 25 on S-A, 10 for the change log on S-C.</param>
    public static Pager Build(int page, int total, Func<int, string> url, int pageSize = SearchRules.PageSize)
    {
        var pageCount = SearchRules.PageCount(total, pageSize);
        var numbers = pageCount <= ShowAllUpTo
            ? Enumerable.Range(1, pageCount).ToList()
            : new[] { 1, page - 1, page, page + 1, pageCount }.Where(n => n >= 1 && n <= pageCount).Distinct().Order().ToList();

        var links = new List<PageLink>();
        for (var i = 0; i < numbers.Count; i++)
        {
            if (i > 0 && numbers[i] > numbers[i - 1] + 1)
            {
                links.Add(new PageLink(null, "", false));
            }

            links.Add(new PageLink(numbers[i], url(numbers[i]), numbers[i] == page));
        }

        var first = total == 0 ? 0 : ((page - 1) * pageSize) + 1;
        var last = Math.Min(page * pageSize, total);
        return new Pager(
            page,
            pageCount,
            first,
            last,
            total,
            page > 1 ? url(page - 1) : null,
            page < pageCount ? url(page + 1) : null,
            links);
    }
}

/// <summary>Screen S-A, story S1: the secretariat searches experts.</summary>
public sealed record StaffSearchPage(
    string? Query,
    string? Error,
    IReadOnlyList<FilterGroup> Groups,
    bool PendingOnly,
    int? Total,
    IReadOnlyList<SearchRow>? Results,
    Pager? Pager,
    string BackUrl = BackLink.Search)
{
    /// <summary>The name link to an expert, carrying this search (text, ticks and page) for S-C's back link.</summary>
    public string ExpertLink(long expertId) => ExpertPage.LinkFrom(expertId, BackUrl);

    /// <summary>Every tick on the page, for "Filters (N ticked)" on a phone.</summary>
    public int TickedCount => Groups.Sum(group => group.TickedCount) + (PendingOnly ? 1 : 0);

    public static StaffSearchPage Build(
        TextService text,
        SearchLists lists,
        string? query,
        SearchFilters filters,
        string? error,
        IReadOnlyList<ExpertHit>? hits,
        int page)
    {
        static string[] AsText(IEnumerable<int> values) =>
            [.. values.Select(value => value.ToString(CultureInfo.InvariantCulture))];

        var statuses = ExpertStatus.All.Select(status =>
            new ListValue(status.ToString(CultureInfo.InvariantCulture), ExpertStatus.Word(status, text)));

        FilterGroup[] groups =
        [
            FilterGroup.Build("region", text["search.group.region"], "", lists.Regions, AsText(filters.Regions)),
            FilterGroup.Build("role", text["search.group.role"], text["search.find.role"], lists.Roles, AsText(filters.Roles)),
            FilterGroup.Build("field", text["search.group.field"], text["search.find.field"], lists.Fields, [.. filters.Fields]),
            FilterGroup.Build("status", text["search.group.status"], "", statuses, AsText(filters.Statuses)),
        ];

        if (hits is null)
        {
            return new StaffSearchPage(query, error, groups, filters.PendingOnly, null, null, null);
        }

        var rows = hits
            .Skip((page - 1) * SearchRules.PageSize)
            .Take(SearchRules.PageSize)
            .Select(hit => new SearchRow(
                hit.Id,
                hit.Name,
                hit.RegistrationId,
                hit.Postcode is null ? null : $"{hit.Postcode} {hit.City}".Trim(),
                ExpertStatus.Word(hit.Status, text),
                ExpertStatus.Icon(hit.Status),
                ExpertStatus.CssClass(hit.Status),
                hit.PendingCount))
            .ToList();

        var pager = Pager.Build(page, hits.Count, number => PageUrl(query, filters, number));
        return new StaffSearchPage(
            query, error, groups, filters.PendingOnly, hits.Count, rows, pager, PageUrl(query, filters, page));
    }

    /// <summary>/staff with the search text, every tick and the page (left out for page 1), properly encoded.</summary>
    public static string PageUrl(string? query, SearchFilters filters, int page)
    {
        var builder = new QueryBuilder();
        if (!string.IsNullOrEmpty(query))
        {
            builder.Add("q", query);
        }

        builder.Add("region", filters.Regions.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        builder.Add("role", filters.Roles.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        builder.Add("field", filters.Fields);
        builder.Add("status", filters.Statuses.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        if (filters.PendingOnly)
        {
            builder.Add("pending", SearchRules.PendingValue);
        }

        if (page > 1)
        {
            builder.Add("page", page.ToString(CultureInfo.InvariantCulture));
        }

        return "/staff" + builder.ToQueryString();
    }
}
