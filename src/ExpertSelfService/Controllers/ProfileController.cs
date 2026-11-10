using System.Globalization;
using ExpertSelfService.Models;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>Story E2, screen X4: the expert's own profile, read-only.</summary>
[Authorize(Policy = SessionClaims.ExpertPolicy)]
public class ProfileController(ProfileService profiles, TextService text) : Controller
{
    // No id in the route or the query: the expert is always the one in the session (SPEC section 2).
    [HttpGet("/profile")]
    public async Task<IActionResult> Index()
    {
        var profile = SessionClaims.ExpertIdOf(User) is long expertId ? profiles.Load(expertId) : null;
        if (profile is null)
        {
            // Removed or set to Retired since logging in: the login ends here, not after 60 minutes.
            await HttpContext.SignOutAsync();
            return Redirect("/");
        }

        var statusKey = ExpertStatus.TextKey(profile.Status);
        var until = profile.Status == ExpertStatus.Standby ? LocalDate.Format(profile.UntilDate) : null;
        return View(new ProfilePage(
            profile,
            LocalDate.Format(profile.Created),
            statusKey is null
                ? string.Format(CultureInfo.CurrentCulture, text["status.unknown"], profile.Status)
                : text[statusKey],
            ExpertStatus.Icon(profile.Status),
            ExpertStatus.CssClass(profile.Status),
            until is null ? null : string.Format(CultureInfo.CurrentCulture, text["profile.until"], until)));
    }
}
