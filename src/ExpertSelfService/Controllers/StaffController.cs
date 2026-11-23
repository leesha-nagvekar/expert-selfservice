using System.Globalization;
using ExpertSelfService.Models;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>Story S1, screen S-A: the secretariat searches experts. The page staff land on after login.</summary>
[Authorize(Policy = SessionClaims.StaffPolicy)]
public class StaffController(ExpertSearchService search, TextService text) : Controller
{
    // A GET form: everything is in the query string, so a search can be bookmarked and the back button works.
    [HttpGet("/staff")]
    public IActionResult Index(
        [FromQuery] string? q,
        [FromQuery] string?[]? region,
        [FromQuery] string?[]? role,
        [FromQuery] string?[]? field,
        [FromQuery] string?[]? status,
        [FromQuery] string? pending,
        [FromQuery] string? page)
    {
        // A page that isn't a plain number comes from a hand-made URL: a 400 like a bad tick.
        if (SearchRules.ReadPage(page) is not { } pageNumber)
        {
            return BadRequest();
        }

        var outcome = search.Search(new SearchRequest(q, region ?? [], role ?? [], field ?? [], status ?? [], pending));
        if (outcome is null)
        {
            return BadRequest();
        }

        // A page past the end is normal use: decisions on S-B or S-C shrink the list, and S-C's back link keeps the
        // old page. Go to the last page, with the text and ticks kept, so the address, the pager and the links agree.
        var lastPage = SearchRules.PageCount(outcome.Hits?.Count ?? 0);
        if (pageNumber > lastPage)
        {
            return Redirect(StaffSearchPage.PageUrl(q, outcome.Filters, lastPage));
        }

        // Too long: the error next to the box. The service ran no search, so there are no results.
        var error = SearchRules.IsTooLong(q)
            ? string.Format(CultureInfo.CurrentCulture, text["search.error.too_long"], SearchRules.MaxLength)
            : null;

        return View(StaffSearchPage.Build(text, outcome.Lists, q, outcome.Filters, error, outcome.Hits, pageNumber));
    }
}
