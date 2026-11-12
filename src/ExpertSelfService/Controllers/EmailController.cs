using System.Globalization;
using ExpertSelfService.Models;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>Story E4, screen X6: change email with a code sent to the new address.</summary>
[Authorize(Policy = SessionClaims.ExpertPolicy)]
public class EmailController(EmailChangeService emails, TextService text, IWebHostEnvironment environment) : Controller
{
    private const string FormPage = "/profile/email";
    private const string CodePageUrl = "/profile/email/code";

    // No id in the route, the query or the form: the expert is always the one in the session (SPEC section 2).
    [HttpGet(FormPage)]
    public IActionResult Edit()
    {
        var current = SessionClaims.ExpertIdOf(User) is long expertId ? emails.CurrentEmail(expertId) : null;
        return current is null ? NotFound() : View("Edit", new EmailChangePage(current, null, null));
    }

    // Only NewEmail is read. Any other field in the post, such as an id, is ignored.
    [HttpPost(FormPage)]
    public IActionResult RequestChange(string? newEmail)
    {
        if (SessionClaims.ExpertIdOf(User) is not long expertId || emails.CurrentEmail(expertId) is not string current)
        {
            return NotFound();
        }

        var normalized = (newEmail ?? "").Trim().ToLowerInvariant();
        var result = emails.Request(expertId, normalized);
        if (result.ErrorKey is string key)
        {
            return View("Edit", new EmailChangePage(current, newEmail, Message(key)));
        }

        if (result.LockedFor is TimeSpan lockedFor)
        {
            return View("Edit", new EmailChangePage(current, newEmail, CodeInput.LockedText(text, lockedFor)));
        }

        return Redirect(CodePageUrl);
    }

    [HttpGet(CodePageUrl)]
    public IActionResult Code()
    {
        var open = SessionClaims.ExpertIdOf(User) is long expertId ? emails.OpenRequest(expertId) : null;
        return open is null ? Redirect(FormPage) : View("Code", CodePage(open, null));
    }

    [HttpPost(CodePageUrl)]
    public IActionResult Confirm(string? code)
    {
        if (SessionClaims.ExpertIdOf(User) is not long expertId || emails.OpenRequest(expertId) is not string open)
        {
            return Redirect(FormPage);
        }

        if (CodeInput.Clean(code) is not string cleaned)
        {
            return View("Code", CodePage(open, text["login.code_format"]));
        }

        var result = emails.Confirm(expertId, cleaned);
        switch (result.Outcome)
        {
            case EmailConfirmOutcome.Confirmed:
                TempData[ProfilePage.SavedSectionKey] = "email";
                return Redirect("/profile");
            case EmailConfirmOutcome.Locked:
                return View("Code", CodePage(open, CodeInput.LockedText(text, result.LockedFor!.Value)));
            case EmailConfirmOutcome.Wrong:
                return View("Code", CodePage(open, text["login.code_wrong"]));
            case EmailConfirmOutcome.InUse:
                // Said only now, after the code proved the expert controls this inbox.
                var current = emails.CurrentEmail(expertId) ?? "";
                return View("Edit", new EmailChangePage(current, open, text["email.error.in_use"]));
            default:
                return Redirect(FormPage);
        }
    }

    private EmailCodePage CodePage(string newEmail, string? error) => new(newEmail, error, environment.IsDevelopment());

    private string Message(string key) => key switch
    {
        "email.error.too_long" => string.Format(CultureInfo.CurrentCulture, text[key], EmailChangeService.EmailMax),
        "email.error.domain" => string.Format(CultureInfo.CurrentCulture, text[key], EmailChangeService.Domain),
        _ => text[key],
    };
}
