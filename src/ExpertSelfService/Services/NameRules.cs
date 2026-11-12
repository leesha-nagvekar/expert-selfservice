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

    // The control-character check and Clean are in TextRules, shared with X8 (E6).
}
