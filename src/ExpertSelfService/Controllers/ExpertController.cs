using ExpertSelfService.Models;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>
/// Story S3, screen S-C: one expert's details, pending changes and change log, for the secretariat.
/// Staff only: unlike the expert pages, the expert id comes from the URL.
/// </summary>
[Authorize(Policy = SessionClaims.StaffPolicy)]
public class ExpertController(
    StaffExpertService experts, PendingListService pending, ChangeLogService changeLog, TextService text) : Controller
{
    [HttpGet("/staff/experts/{id:long}")]
    public IActionResult Index(long id, [FromQuery] string? page)
    {
        // The expert first: an unknown expert is "Page not found" whatever the page says.
        if (experts.Load(id) is not { } profile)
        {
            return NotFound();
        }

        // Same rules as S-A: a page that isn't plain digits, or is past the last page, comes from a hand-made URL.
        if (SearchRules.ReadPage(page) is not { } pageNumber
            || pageNumber > SearchRules.PageCount(changeLog.Count(id), ChangeLogService.PageSize))
        {
            return BadRequest();
        }

        return View(ExpertPage.Build(text, id, profile, pending.ForExpert(id), changeLog.Page(id, pageNumber), pageNumber));
    }
}
