using System.Globalization;

namespace ExpertSelfService.Services;

/// <summary>Rules for one-line text fields, shared by X7 (name) and X8 (company and job title).</summary>
public static class TextRules
{
    /// <summary>
    /// True when the text holds a line break or another control character: C0 and C1 controls (\n, \r, \t, \0, DEL ...)
    /// and the Unicode line and paragraph separators. Letters from any language, accents (also combining ones),
    /// spaces, hyphens and apostrophes are fine (decisions.md, E5).
    /// </summary>
    public static bool HasControlCharacter(string text) =>
        text.Any(c => char.IsControl(c)
            || char.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    /// <summary>Trimmed; empty becomes null, so a cleared field is stored as nothing, not "".</summary>
    public static string? Clean(string? value) => value?.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}
