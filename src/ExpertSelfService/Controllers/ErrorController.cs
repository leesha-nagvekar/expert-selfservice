using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

// A POST refused for a missing token is shown again here as a POST. Without this,
// the check would fail a second time and the visitor would get a blank page.
[IgnoreAntiforgeryToken]
public class ErrorController : Controller
{
    // [Route] without [HttpGet]: a failed POST must reach this page too.
    [Route("/error/{code:int}")]
    public IActionResult Show(int code)
    {
        // Only real error codes. Someone typing /error/200 gets "Page not found".
        Response.StatusCode = code is >= 400 and < 600 ? code : StatusCodes.Status404NotFound;

        // Every 4xx looks like "Page not found", so a refusal gives nothing away (SPEC section 2).
        var isServerError = Response.StatusCode >= 500;
        return View("Error", isServerError);
    }
}
