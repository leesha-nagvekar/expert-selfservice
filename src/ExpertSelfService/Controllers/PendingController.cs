using System.Globalization;
using ExpertSelfService.Models;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>Story S2, screen S-B: the secretariat approves or rejects pending changes.</summary>
[Authorize(Policy = SessionClaims.StaffPolicy)]
public class PendingController(PendingListService list, DecisionService decisions, TextService text) : Controller
{
    private const string Page = "/staff/pending";

    // The message after a decision lives in TempData, shown once, like "Saved" on the expert pages (same keys as S-C).
    private const string MessageKindKey = DecisionMessage.KindKey;
    private const string MessageTextKey = DecisionMessage.TextKey;

    [HttpGet(Page)]
    public IActionResult Index([FromQuery] string? q) => View(Build(q));

    [HttpPost(Page + "/{id:long}/approve")]
    public IActionResult Approve(long id, [FromForm] string? q)
    {
        var result = decisions.Approve(id, StaffId());
        return After(result, DecisionMessage.Approved, "pending.approved_message", q);
    }

    [HttpPost(Page + "/{id:long}/reject")]
    public IActionResult Reject(long id, [FromForm] string? reason, [FromForm] string? q)
    {
        if (DecisionService.ReasonError(reason) is not { } errorKey)
        {
            return After(decisions.Reject(id, StaffId(), reason!), DecisionMessage.Rejected, "pending.rejected_message", q);
        }

        // A bad reason decides nothing. Unknown or already decided still says so, as a good reason would.
        var status = decisions.StatusOf(id);
        if (status != DecisionOutcome.Done)
        {
            return After(new DecisionResult(status), DecisionMessage.Rejected, "pending.rejected_message", q);
        }

        // The page again, with the error in that row and the reason kept. If the search text hid the row, the
        // whole list is shown instead, so the error is never lost.
        var page = Build(q, readMessage: false);
        if (page.Rows?.Any(row => row.Id == id) != true)
        {
            page = Build(null, readMessage: false);
        }

        var error = string.Format(CultureInfo.CurrentCulture, text[errorKey], DecisionService.MaxReason);
        return View("Index", page with { ErrorRowId = id, ReasonError = error, Reason = reason });
    }

    private IActionResult After(DecisionResult result, string kind, string messageKey, string? q)
    {
        switch (result.Outcome)
        {
            case DecisionOutcome.NotFound:
                return NotFound();
            case DecisionOutcome.AlreadyDecided:
                TempData[MessageKindKey] = DecisionMessage.AlreadyDecided;
                TempData[MessageTextKey] = text["pending.already_decided"];
                break;
            default:
                TempData[MessageKindKey] = kind;
                TempData[MessageTextKey] = string.Format(
                    CultureInfo.CurrentCulture, text[messageKey], result.ExpertName, PendingPage.FieldLabel(text, result.Field));
                break;
        }

        // The search text comes back with the redirect, so the list stays as it was.
        return Redirect(string.IsNullOrWhiteSpace(q) ? Page : $"{Page}?q={Uri.EscapeDataString(q)}");
    }

    private PendingPage Build(string? q, bool readMessage = true)
    {
        var message = readMessage && TempData[MessageKindKey] is string kind && TempData[MessageTextKey] is string messageText
            ? new DecisionMessage(kind, messageText)
            : null;

        if (SearchRules.IsTooLong(q))
        {
            var error = string.Format(CultureInfo.CurrentCulture, text["search.error.too_long"], SearchRules.MaxLength);
            return PendingPage.Build(text, q, error, null, message);
        }

        return PendingPage.Build(text, q, null, list.List(q), message);
    }

    // The policy guarantees the claim; a missing one would be a bug, not a user error.
    private long StaffId() =>
        SessionClaims.StaffIdOf(User) ?? throw new InvalidOperationException("Staff page without a staff id");
}
