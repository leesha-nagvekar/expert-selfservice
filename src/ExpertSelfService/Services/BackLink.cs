namespace ExpertSelfService.Services;

/// <summary>
/// Story S3, screen S-C: where the back link above the expert's name goes. The list's URL arrives as ?back=, so anyone
/// can type anything there. Only S-A and S-B are kept: exactly /staff or /staff/pending, either with a query after a
/// "?". Anything else is the search (/staff): another site, another page, path tricks, backslashes, control characters,
/// or a value over <see cref="MaxLength"/> characters. It is only ever a link's href, never a redirect target.
/// </summary>
public static class BackLink
{
    /// <summary>Where the link goes without a (usable) back: the search.</summary>
    public const string Search = "/staff";

    public const string Pending = "/staff/pending";

    /// <summary>Longer than any URL S-A builds with a fair number of ticks.</summary>
    public const int MaxLength = 2000;

    /// <summary><paramref name="value"/> when it is S-A or S-B's own URL, else <see cref="Search"/>.</summary>
    public static string Safe(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength
            || value.Contains('\\') || value.Any(char.IsControl))
        {
            return Search;
        }

        // The path is everything before the first "?". It must be exactly one of the two lists: that rules out other
        // sites ("//x", "https://x"), other pages, "/staff/", "/staffx", "/staff#x" and every "/../" trick at once.
        var question = value.IndexOf('?', StringComparison.Ordinal);
        var path = question < 0 ? value : value[..question];
        return path is Search or Pending ? value : Search;
    }

    /// <summary>True when the link points to S-B, so it says "Back to pending approvals".</summary>
    public static bool IsPending(string safe) => safe == Pending || safe.StartsWith(Pending + "?", StringComparison.Ordinal);

    /// <summary>
    /// "back=…" for S-C's own URLs, or null when the link goes to the plain search anyway, so the URL stays short.
    /// </summary>
    public static string? Query(string safe) => safe == Search ? null : "back=" + Uri.EscapeDataString(safe);
}
