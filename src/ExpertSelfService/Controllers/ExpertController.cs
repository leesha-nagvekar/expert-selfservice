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

        return View(Build(id, profile, pageNumber, ReadMessage()));
    }

    [HttpPost("/staff/experts/{id:long}/changes/{changeId:long}/approve")]
    public IActionResult Approve(long id, long changeId) =>
        After(id, decisions.Approve(changeId, StaffId(), expertId: id), DecisionMessage.Approved, "pending.approved_message");

    [HttpPost("/staff/experts/{id:long}/changes/{changeId:long}/reject")]
    public IActionResult Reject(long id, long changeId, [FromForm] string? reason)
    {
        if (DecisionService.ReasonError(reason) is not { } errorKey)
        {
            return After(
                id, decisions.Reject(changeId, StaffId(), reason!, expertId: id), DecisionMessage.Rejected, "pending.rejected_message");
        }

        // A bad reason decides nothing. Unknown, another expert's or already decided still says so, as a good reason would.
        var status = decisions.StatusOf(changeId, expertId: id);
        if (status != DecisionOutcome.Done)
        {
            return After(id, new DecisionResult(status), DecisionMessage.Rejected, "pending.rejected_message");
        }

        // StatusOf found the change under this expert, so the expert exists; the null check is only for the compiler.
        if (experts.Load(id) is not { } profile)
        {
            return NotFound();
        }

        // The page again (log page 1), with the error in that row and the reason kept.
        var error = string.Format(CultureInfo.CurrentCulture, text[errorKey], DecisionService.MaxReason);
        return View("Index", Build(id, profile, 1, null) with { ErrorRowId = changeId, ReasonError = error, Reason = reason });
    }

    private IActionResult After(long id, DecisionResult result, string kind, string messageKey)
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

        // Back to the expert page, log page 1: the newest entry (an approval) is at the top.
        return Redirect(ExpertPage.Url(id));
    }

    private ExpertPage Build(long id, ExpertProfile profile, int pageNumber, DecisionMessage? message) =>
        ExpertPage.Build(text, id, profile, pending.ForExpert(id), changeLog.Page(id, pageNumber), pageNumber)
            with
        { Message = message };

    private DecisionMessage? ReadMessage() =>
        TempData[DecisionMessage.KindKey] is string kind && TempData[DecisionMessage.TextKey] is string messageText
            ? new DecisionMessage(kind, messageText)
            : null;

    // The policy guarantees the claim; a missing one would be a bug, not a user error.
    private long StaffId() =>
        SessionClaims.StaffIdOf(User) ?? throw new InvalidOperationException("Staff page without a staff id");
}
