using ExpertSelfService.Services;

namespace ExpertSelfService.Models;

/// <summary>
/// Screen X4. The profile plus what the view shows ready-made: dates in the chosen language,
/// and the status as word, icon and colour class.
/// </summary>
public sealed record ProfilePage(
    ExpertProfile Profile,
    string? Created,
    string StatusText,
    string StatusIcon,
    string StatusClass,
    string? StatusUntil)
{
    public PendingState? State(string field) => Profile.States.GetValueOrDefault(field);
}
