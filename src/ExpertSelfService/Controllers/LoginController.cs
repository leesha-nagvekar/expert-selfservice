using System.Globalization;
using System.Security.Claims;
using ExpertSelfService.Models;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>Story E1: X1 (email) and X2 (code). The rules are in LoginService.</summary>
public class LoginController(LoginService login, TextService text, IWebHostEnvironment environment) : Controller
{
    // The email between X1 and X2 lives in TempData: an encrypted cookie, not the URL or a form field.
    private const string EmailKey = "login_email";

    private const int MaxEmailLength = 254;

    private const string EmailView = "~/Views/Home/Index.cshtml";

    private CodePage CodePage(string? error) => new(error, environment.IsDevelopment());

    // The language switch returns to the current address, which after a failed POST is /login.
    [HttpGet("/login")]
    public IActionResult Start() => Redirect("/");

    [HttpPost("/login")]
    public IActionResult RequestCode(string? email)
    {
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        if (normalized.Length is 0 or > MaxEmailLength)
        {
            return View(EmailView, new EmailPage(email, text["login.email_required"]));
        }

        if (login.RequestCode(normalized) is TimeSpan lockedFor)
        {
            return View(EmailView, new EmailPage(email, LockedText(lockedFor)));
        }

        // The same answer whether or not the email is registered (SPEC X1).
        TempData[EmailKey] = normalized;
        return Redirect("/login/code");
    }

    [HttpGet("/login/code")]
    public IActionResult Code()
    {
        if (SessionClaims.Landing(User) is string landing)
        {
            return Redirect(landing);
        }

        return TempData.Peek(EmailKey) is string ? View(CodePage(null)) : Redirect("/");
    }

    [HttpPost("/login/code")]
    public async Task<IActionResult> SubmitCode(string? code)
    {
        if (TempData.Peek(EmailKey) is not string email)
        {
            return Redirect("/");
        }

        // All whitespace goes, so "123 456" and pasted mail text with a non-breaking space work.
        // Then ASCII digits only: .NET's \d and char.IsDigit also accept digits from other scripts.
        code = string.Concat((code ?? "").Where(character => !char.IsWhiteSpace(character)));
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            return View("Code", CodePage(text["login.code_format"]));
        }

        var result = login.CheckCode(email, code);
        if (result.LockedFor is TimeSpan lockedFor)
        {
            return View("Code", CodePage(LockedText(lockedFor)));
        }

        if (result.Person is not Person person)
        {
            return View("Code", CodePage(text["login.code_wrong"]));
        }

        TempData.Remove(EmailKey);
        var identity = new ClaimsIdentity([SessionClaims.For(person)], CookieAuthenticationDefaults.AuthenticationScheme);
        // Not persistent: the cookie ends when the browser closes. Signing in replaces any earlier login.
        await HttpContext.SignInAsync(new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false });
        return Redirect(SessionClaims.LandingFor(person));
    }

    [HttpPost("/logout")]
    public async Task<IActionResult> LogOut()
    {
        await HttpContext.SignOutAsync();
        return Redirect("/");
    }

    private string LockedText(TimeSpan lockedFor)
    {
        var minutes = (int)Math.Ceiling(lockedFor.TotalMinutes);
        return minutes == 1
            ? text["login.locked_one"]
            : string.Format(CultureInfo.CurrentCulture, text["login.locked"], minutes);
    }
}
