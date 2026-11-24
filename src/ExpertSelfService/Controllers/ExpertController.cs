using System.Globalization;
using ExpertSelfService.Models;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>
/// Story S3, screen S-C: one expert's details, pending changes and change log, for the secretariat, who can approve
/// or reject the expert's pending changes here as on S-B. Staff only: unlike the expert pages, the expert id comes
/// from the URL, and a decision only counts for a change that belongs to that expert.
/// The back link to the list as it was left arrives as "back" (query or form) and is checked once, by BackLink.Safe.
/// </summary>
[Authorize(Policy = SessionClaims.StaffPolicy)]
public class ExpertController(
    StaffExpertService experts,
    PendingListService pending,
    ChangeLogService changeLog,
    DecisionService decisions,
    TextService text) : Controller
{
    [HttpGet("/staff/experts/{id:long}")]
    public IActionResult Index(long id, [FromQuery] string? page, [FromQuery] string? back)
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

        // A bad back is no error: the link simply goes to the search.
        return View(Build(id, profile, pageNumber, BackLink.Safe(back), ReadMessage()));
    }

    [HttpPost("/staff/experts/{id:long}/changes/{changeId:long}/approve")]
    public IActionResult Approve(long id, long changeId, [FromForm] string? back) =>
        After(
            id, BackLink.Safe(back), decisions.Approve(changeId, StaffId(), expertId: id),
            DecisionMessage.Approved, "pending.approved_message");

    [HttpPost("/staff/experts/{id:long}/changes/{changeId:long}/reject")]
    public IActionResult Reject(long id, long changeId, [FromForm] string? reason, [FromForm] string? back)
    {
        var safeBack = BackLink.Safe(back);
        if (DecisionService.ReasonError(reason) is not { } errorKey)
        {
            return After(
                id, safeBack, decisions.Reject(changeId, StaffId(), reason!, expertId: id),
                DecisionMessage.Rejected, "pending.rejected_message");
        }

        // A bad reason decides nothing. Unknown, another expert's or already decided still says so, as a good reason would.
        var status = decisions.StatusOf(changeId, expertId: id);
        if (status != DecisionOutcome.Done)
        {
            return After(id, safeBack, new DecisionResult(status), DecisionMessage.Rejected, "pending.rejected_message");
        }

        // StatusOf found the change under this expert, so the expert exists; the null check is only for the compiler.
        if (experts.Load(id) is not { } profile)
        {
            return NotFound();
        }

        // The page again (log page 1), with the error in that row, the reason and the back link kept.
        var error = string.Format(CultureInfo.CurrentCulture, text[errorKey], DecisionService.MaxReason);
        var page = Build(id, profile, 1, safeBack, null);
        // Shown at the POST address, which has no GET: the language switch returns to the expert page, back link kept.
        ViewData[LanguageLinks.NextKey] = ExpertPage.PageUrl(id, 1, safeBack);
        return View("Index", page with { ErrorRowId = changeId, ReasonError = error, Reason = reason });
    }

    private IActionResult After(long id, string back, DecisionResult result, string kind, string messageKey)
    {
        switch (result.Outcome)
        {
            case DecisionOutcome.NotFound:
                return NotFound();
            case DecisionOutcome.AlreadyDecided:
                TempData[DecisionMessage.KindKey] = DecisionMessage.AlreadyDecided;
                TempData[DecisionMessage.TextKey] = text["pending.already_decided"];
                break;
            default:
                TempData[DecisionMessage.KindKey] = kind;
                TempData[DecisionMessage.TextKey] = string.Format(
                    CultureInfo.CurrentCulture, text[messageKey], result.ExpertName, FieldLabels.For(text, result.Field));
                break;
        }

        // Back to the expert page, log page 1 (the newest entry, an approval, is at the top), keeping the back link.
        // The redirect is always to S-C itself; back only ever travels inside its query.
        return Redirect(ExpertPage.PageUrl(id, 1, back));
    }

    private ExpertPage Build(long id, ExpertProfile profile, int pageNumber, string back, DecisionMessage? message)
    {
        var page = ExpertPage.Build(
            text, id, profile, pending.ForExpert(id), changeLog.Page(id, pageNumber), pageNumber, back);
        return page with { Message = message };
    }

    private DecisionMessage? ReadMessage() =>
        TempData[DecisionMessage.KindKey] is string kind && TempData[DecisionMessage.TextKey] is string messageText
            ? new DecisionMessage(kind, messageText)
            : null;

    // The policy guarantees the claim; a missing one would be a bug, not a user error.
    private long StaffId() =>
        SessionClaims.StaffIdOf(User) ?? throw new InvalidOperationException("Staff page without a staff id");
}
