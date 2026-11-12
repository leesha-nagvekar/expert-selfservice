using System.Globalization;
using ExpertSelfService.Models;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>Story E5, screen X7: edit name. Goes to the secretariat for approval (SPEC section 3).</summary>
[Authorize(Policy = SessionClaims.ExpertPolicy)]
public class NameController(NameService names, TextService text) : Controller
{
    private const string EditView = "Edit";

    // No id in the route, the query or the form: the expert is always the one in the session (SPEC section 2).
    [HttpGet("/profile/name")]
    public IActionResult Edit()
    {
        var page = SessionClaims.ExpertIdOf(User) is long expertId ? names.Load(expertId) : null;
        return page is null ? NotFound() : View(EditView, page);
    }

    // Only these three fields are read. Any other field in the post, such as an id, is ignored.
    [HttpPost("/profile/name")]
    public IActionResult Send(string? firstName, string? middleName, string? lastName)
    {
        if (SessionClaims.ExpertIdOf(User) is not long expertId || names.Load(expertId) is not NamePage page)
        {
            return NotFound();
        }

        var form = NameForm.From(firstName, middleName, lastName);
        var errorKeys = NameService.Validate(form, page.IsLocked);
        if (errorKeys.Count > 0)
        {
            var messages = errorKeys.ToDictionary(e => e.Key, e => Message(e.Value));
            return View(EditView, page with { Form = form, Errors = messages });
        }

        // Sent or nothing changed: both go back to the profile, which shows "Pending approval" for each sent field.
        return names.Send(expertId, form) == SendResult.ExpertNotFound ? NotFound() : Redirect("/profile");
    }

    private string Message(string key) => key == "name.error.too_long"
        ? string.Format(CultureInfo.CurrentCulture, text[key], NameRules.Max)
        : text[key];
}
