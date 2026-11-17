using System.Globalization;

namespace ExpertSelfService.Services;

/// <summary>SPEC S-A: what the secretariat's search accepts.</summary>
public static class SearchRules
{
    /// <summary>The search box's max length: the same as an email address (X1).</summary>
    public const int MaxLength = 254;

    /// <summary>The value of the "Only experts with pending changes" tick box.</summary>
    public const string PendingValue = "1";

    /// <summary>Results per page.</summary>
    public const int PageSize = 25;

    public static bool IsTooLong(string? query) => query is not null && query.Length > MaxLength;

    /// <summary>How many pages the results fill. Always at least 1, so an empty result is page 1 of 1.</summary>
    public static int PageCount(int total) => Math.Max(1, (total + PageSize - 1) / PageSize);

    /// <summary>
    /// The page number from the query string: absent or empty is page 1. Only plain digits without a leading zero
    /// count ("07", "+2", "1.5" and "0" are null, a 400). Whether the page exists is checked against the results.
    /// </summary>
    public static int? ReadPage(string? page)
    {
        if (string.IsNullOrEmpty(page))
        {
            return 1;
        }

        if (page.Length > 6 || page[0] == '0' || !page.All(char.IsAsciiDigit))
        {
            return null;
        }

        return int.Parse(page, CultureInfo.InvariantCulture);
    }

    /// <summary>The search box split into words. Spaces around and between words don't count.</summary>
    public static List<string> Words(string? query) =>
        [.. (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>
    /// The ticked values as filters, or null when a value isn't in its list. The page only offers listed values,
    /// so anything else comes from a hand-made request (a 400, like X9). Duplicates count once.
    /// </summary>
    public static SearchFilters? Read(
        SearchLists lists, string? query, string?[] regions, string?[] roles, string?[] fields, string?[] statuses, string? pending)
    {
        var regionIds = Ids(regions, lists.Regions.Select(region => region.Value));
        var roleIds = Ids(roles, lists.Roles.Select(role => role.Value));
        var statusIds = Ids(statuses, ExpertStatus.All.Select(status => status.ToString(CultureInfo.InvariantCulture)));
        var known = lists.Fields.Select(field => field.Value).ToHashSet(StringComparer.Ordinal);
        var fieldNames = fields.All(field => field is not null && known.Contains(field))
            ? fields.OfType<string>().Distinct(StringComparer.Ordinal).ToList()
            : null;

        if (regionIds is null || roleIds is null || statusIds is null || fieldNames is null
            || (pending is not null && pending != PendingValue))
        {
            return null;
        }

        return new SearchFilters(Words(query), regionIds, roleIds, fieldNames, statusIds, pending == PendingValue);
    }

    // Compared as text against the list's own values, so "07" or "+7" isn't read as 7.
    private static List<int>? Ids(string?[] values, IEnumerable<string> allowed)
    {
        var known = allowed.ToHashSet(StringComparer.Ordinal);
        if (!values.All(value => value is not null && known.Contains(value)))
        {
            return null;
        }

        return [.. values.OfType<string>().Distinct(StringComparer.Ordinal).Select(value => int.Parse(value, CultureInfo.InvariantCulture))];
    }
}
