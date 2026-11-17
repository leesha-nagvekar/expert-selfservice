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
}
