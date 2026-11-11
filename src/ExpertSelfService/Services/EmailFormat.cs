using System.Text.RegularExpressions;

namespace ExpertSelfService.Services;

/// <summary>
/// A plain check that an email looks like one (SPEC X1, and later X6). The C# copy of rules.EMAIL_PATTERN:
/// EmailFormatTests reads rules.py, so the two can't drift apart unnoticed.
/// ASCII only, because NOCASE and SQLite's lower() fold A-Z only (decisions.md, "Emails are compared without case").
/// </summary>
public static partial class EmailFormat
{
    /// <summary>
    /// "local@domain.tld": a name part of letters A-Z, digits and . _ % + -, one @, and a domain of
    /// letters A-Z, digits and - with at least one dot and no empty part.
    /// </summary>
    public static bool IsValid(string email) => Pattern().IsMatch(email);

    // The same pattern as rules.EMAIL_PATTERN, anchored to the whole text like Python's fullmatch.
    [GeneratedRegex(@"^(?:[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+)\z")]
    private static partial Regex Pattern();
}
