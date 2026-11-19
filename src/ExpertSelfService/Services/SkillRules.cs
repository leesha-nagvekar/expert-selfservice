namespace ExpertSelfService.Services;

/// <summary>SPEC X9: how a list of fields of expertise is stored in ss_pending_changes.</summary>
public static class SkillRules
{
    /// <summary>The ss_pending_changes.field value for the whole list.</summary>
    public const string Field = "Skills";

    /// <summary>Between the names. No Fags name contains ";" (check_data.py checks it), so S2 can split the list again.</summary>
    public const string Separator = "; ";

    /// <summary>
    /// The names without empties and duplicates, in Danish order whatever the page's language, so the same choice
    /// is always the same text. Ordinal breaks a tie, so two names that differ only in case still have one fixed order.
    /// </summary>
    public static List<string> Sorted(IEnumerable<string?> names) =>
        [.. names
            .OfType<string>()
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(DanishOrder.Comparer)
            .ThenBy(name => name, StringComparer.Ordinal)];

    /// <summary>The list as stored: "EL; Fiber; Varmepumpe".</summary>
    public static string Join(IEnumerable<string?> names) => string.Join(Separator, Sorted(names));

    /// <summary>The opposite of <see cref="Join"/>: "EL; Fiber" gives EL and Fiber. Empty or null gives none.</summary>
    public static List<string> Split(string? list) =>
        Sorted((list ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// What a change adds and removes, from the list the expert had when sending to the list they sent. S-B shows
    /// it and approving applies exactly it (S2), so the screen and the database can't disagree.
    /// </summary>
    public static (List<string> Added, List<string> Removed) Diff(string? oldList, string? newList)
    {
        var before = Split(oldList);
        var after = Split(newList);
        return ([.. after.Except(before, StringComparer.Ordinal)], [.. before.Except(after, StringComparer.Ordinal)]);
    }
}
