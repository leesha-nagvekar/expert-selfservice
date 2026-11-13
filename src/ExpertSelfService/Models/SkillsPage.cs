using System.Globalization;
using ExpertSelfService.Services;

namespace ExpertSelfService.Models;

/// <summary>One tick box on X9. Old: the expert has it, but it is no longer in Fags (decisions.md, E7).</summary>
public sealed record SkillChoice(string Name, bool IsChecked, bool IsOld);

/// <summary>
/// X9: the expert's current fields, VBA's list (Fags), what is ticked (the current fields, or what was sent),
/// the newest pending or rejected state, and the error, if any.
/// </summary>
public sealed record SkillsPage(
    IReadOnlyList<string> Current,
    IReadOnlyList<string> Fags,
    IReadOnlyList<string> Chosen,
    PendingState? State,
    string? Error)
{
    /// <summary>While a change is pending, the list can't be edited again (SPEC section 3).</summary>
    public bool IsLocked => State?.IsPending == true;

    /// <summary>A name the expert may send: on VBA's list, or one of their own current fields.</summary>
    public bool IsAllowed(string name) => Fags.Contains(name, StringComparer.Ordinal) || Current.Contains(name, StringComparer.Ordinal);

    /// <summary>Every Fags name plus the expert's old ones, sorted for the page's language, as the profile does.</summary>
    public IReadOnlyList<SkillChoice> Choices =>
        [.. Fags.Union(Current, StringComparer.Ordinal)
            .Order(StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
            .Select(name => new SkillChoice(
                name,
                Chosen.Contains(name, StringComparer.Ordinal),
                !Fags.Contains(name, StringComparer.Ordinal)))];

    /// <summary>The current fields, sorted for the page's language, for the locked screen.</summary>
    public IReadOnlyList<string> CurrentSorted =>
        [.. Current.Order(StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))];
}
