namespace ExpertSelfService.Services;

/// <summary>
/// Candidates.Status: the C# copy of the codes in rules.py (decisions.md, "Data rules in two languages").
/// StatusTests reads rules.py, so the two copies can't drift apart unnoticed.
/// </summary>
public static class ExpertStatus
{
    public const int Standby = -1;
    public const int Retired = 0;
    public const int Active = 1;
    public const int Accepted = 2;
    public const int Observation = 3;
    public const int Evaluated = 4;

    /// <summary>The i18n key of the status name, or null for a number the app doesn't know.</summary>
    public static string? TextKey(int status) => status switch
    {
        Standby => "status.standby",
        Retired => "status.retired",
        Active => "status.active",
        Accepted => "status.accepted",
        Observation => "status.observation",
        Evaluated => "status.evaluated",
        _ => null,
    };

    /// <summary>Status is always icon + colour + word (guide section 5). The icon name for Views/Shared/_Icon.cshtml.</summary>
    public static string Icon(int status) => status switch
    {
        Active or Accepted or Evaluated => "check",
        Observation => "eye",
        Standby => "pause",
        _ => "info",
    };

    /// <summary>The CSS class that sets the colour from tokens.css.</summary>
    public static string CssClass(int status) => status switch
    {
        Active or Accepted or Evaluated => "status-ok",
        Observation => "status-info",
        Standby => "status-standby",
        _ => "status-unknown",
    };
}
