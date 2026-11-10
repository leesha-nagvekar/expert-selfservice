using ExpertSelfService.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

public class LanguageController : Controller
{
    [HttpGet("/language/{code}")]
    public IActionResult Set(string code, string? next)
    {
        if (!TextService.Languages.Contains(code))
        {
            return NotFound();
        }

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            // Format culture (en-GB) for dates and numbers, language (en) for the texts.
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(TextService.FormatCulture(code).Name, code)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
            });

        // Only an address on this site. Anything else could send people to another site.
        return LocalRedirect(Url.IsLocalUrl(next) ? next : "/");
    }
}
