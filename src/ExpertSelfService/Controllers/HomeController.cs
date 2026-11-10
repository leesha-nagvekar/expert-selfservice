using ExpertSelfService.Models;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

public class HomeController : Controller
{
    /// <summary>The front page is X1, the email form. Someone already logged in goes to their landing page.</summary>
    [HttpGet("/")]
    public IActionResult Index() =>
        SessionClaims.Landing(User) is string landing
            ? Redirect(landing)
            : View(new EmailPage(null, null));
}
