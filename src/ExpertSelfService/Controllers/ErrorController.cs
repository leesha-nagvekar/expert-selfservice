using System.Globalization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

// A POST refused for a missing token is shown again here as a POST. Without this,
// the check would fail a second time and the visitor would get a blank page.
[IgnoreAntiforgeryToken]
public class ErrorController : Controller
{
    // [Route] without [HttpGet]: a failed POST must reach this page too.
    // The code is read from the address itself, with no model binding. The failed POST's form is still there:
    // a field named "Code" (X2, X6b) would win over the address (E4), and a form ASP.NET can't read, such as one
    // with a NUL character, would leave the code at 0 (E5). Both showed 404 instead of the real status.
#pragma warning disable ASP0018 // {code} is read from RouteData on purpose, see above: a parameter would be model-bound.
    [Route("/error/{code:int}")]
#pragma warning restore ASP0018
    public IActionResult Show()
    {
        var code = int.TryParse(RouteData.Values["code"] as string, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : StatusCodes.Status404NotFound;

        // Only real error codes. Someone typing /error/200 gets "Page not found".
        Response.StatusCode = code is >= 400 and < 600 ? code : StatusCodes.Status404NotFound;

        // Every 4xx looks like "Page not found", so a refusal gives nothing away (SPEC section 2).
        var isServerError = Response.StatusCode >= 500;
        return View("Error", isServerError);
    }
}
