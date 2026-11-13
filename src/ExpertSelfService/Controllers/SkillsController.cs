using ExpertSelfService.Models;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>Story E7, screen X9: edit fields of expertise. Goes to the secretariat for approval (SPEC section 3).</summary>
[Authorize(Policy = SessionClaims.ExpertPolicy)]
public class SkillsController(SkillService service, TextService text) : Controller
{
    private const string EditView = "Edit";

    // No id in the route, the query or the form: the expert is always the one in the session (SPEC section 2).
    [HttpGet("/profile/skills")]
    public IActionResult Edit()
    {
        var page = SessionClaims.ExpertIdOf(User) is long expertId ? service.Load(expertId) : null;
        return page is null ? NotFound() : View(EditView, page);
    }

    // Only the ticked names are read. Any other field in the post, such as an id, is ignored.
    [HttpPost("/profile/skills")]
    public IActionResult Send(string?[]? skills)
    {
        if (SessionClaims.ExpertIdOf(User) is not long expertId || service.Load(expertId) is not SkillsPage page)
        {
            return NotFound();
        }

        // Pending: nothing can be sent, whatever the post says. SkillService checks it again when writing.
        if (page.IsLocked)
        {
            return Redirect("/profile");
        }

        var chosen = skills ?? [];

        // A name that isn't on VBA's list or one of the expert's own can't come from the page: a hand-made post.
        if (chosen.Any(name => name is null || !page.IsAllowed(name)))
        {
            return BadRequest();
        }

        var names = chosen.OfType<string>().ToList();
        if (names.Count == 0)
        {
            return View(EditView, page with { Chosen = names, Error = text["skills.error.required"] });
        }

        // Sent or nothing changed: both go back to the profile, which shows "Pending approval" when sent.
        return service.Send(expertId, names) == SendResult.ExpertNotFound ? NotFound() : Redirect("/profile");
    }
}
