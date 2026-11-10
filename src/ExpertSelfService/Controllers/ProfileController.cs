using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>The expert's landing page. Only a heading for now: story E2 fills it in.</summary>
[Authorize(Policy = SessionClaims.ExpertPolicy)]
public class ProfileController : Controller
{
    [HttpGet("/profile")]
    public IActionResult Index() => View();
}
