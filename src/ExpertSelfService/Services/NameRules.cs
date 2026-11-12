using System.Globalization;

namespace ExpertSelfService.Services;

/// <summary>The rules of SPEC X7. NameTests reads rules.py, so the limit can't drift from rules.MAX_LENGTHS.</summary>
public static class NameRules
{
    public const string FirstName = "FirstName";
    public const string MiddleName = "MiddleName";
    public const string LastName = "LastName";

    /// <summary>In the order of the form. Each is one ss_pending_changes.field value.</summary>
    public static readonly IReadOnlyList<string> Fields = [FirstName, MiddleName, LastName];

    /// <summary>The C# copy of rules.MAX_LENGTHS for FirstName, MiddleName and LastName.</summary>
    public const int Max = 50;

    /// <summary>
    /// True when the text holds a line break or another control character: C0 and C1 controls (\n, \r, \t, \0, DEL ...)
    /// and the Unicode line and paragraph separators. Letters from any language, accents (also combining ones),
    /// spaces, hyphens and apostrophes are fine (decisions.md, E5).
    /// </summary>
    public static bool HasControlCharacter(string text) =>
        text.Any(c => char.IsControl(c)
            || char.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    /// <summary>Trimmed; empty becomes null, so a removed middle name is stored as nothing, not "".</summary>
    public static string? Clean(string? value) => value?.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}
