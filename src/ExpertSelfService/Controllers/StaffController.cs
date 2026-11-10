using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>The secretariat's landing page. Only a heading for now: story S1 (search experts) replaces it.</summary>
[Authorize(Policy = SessionClaims.StaffPolicy)]
public class StaffController : Controller
{
    [HttpGet("/staff")]
    public IActionResult Index() => View();
}
