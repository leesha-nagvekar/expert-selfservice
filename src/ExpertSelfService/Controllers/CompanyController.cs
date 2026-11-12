using System.Globalization;
using ExpertSelfService.Models;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>Story E6, screen X8: edit company, CVR number and job title. Saved at once (SPEC section 3).</summary>
[Authorize(Policy = SessionClaims.ExpertPolicy)]
public class CompanyController(CompanyService companies, TextService text) : Controller
{
    private const string EditView = "Edit";

    // No id in the route, the query or the form: the expert is always the one in the session (SPEC section 2).
    [HttpGet("/profile/company")]
    public IActionResult Edit()
    {
        var page = SessionClaims.ExpertIdOf(User) is long expertId ? companies.Load(expertId) : null;
        return page is null ? NotFound() : View(EditView, page);
    }

    // Only these three fields are read. Any other field in the post, such as an id, roles or status, is ignored.
    [HttpPost("/profile/company")]
    public IActionResult Save(string? company, string? cvr, string? designation)
    {
        if (SessionClaims.ExpertIdOf(User) is not long expertId)
        {
            return NotFound();
        }

        var form = CompanyForm.From(company, cvr, designation);
        var errorKeys = CompanyService.Validate(form);
        if (errorKeys.Count > 0)
        {
            var messages = errorKeys.ToDictionary(e => e.Key, e => Message(e.Value));
            return View(EditView, new CompanyPage(form, companies.Roles(expertId), messages));
        }

        switch (companies.Save(expertId, form))
        {
            case SaveResult.ExpertNotFound:
                return NotFound();
            case SaveResult.Saved:
                TempData[ProfilePage.SavedSectionKey] = "company";
                break;
        }

        return Redirect("/profile");
    }

    private string Message(string key) => key switch
    {
        "company.error.company_too_long" => string.Format(CultureInfo.CurrentCulture, text[key], CompanyRules.CompanyMax),
        "company.error.designation_too_long" => string.Format(CultureInfo.CurrentCulture, text[key], CompanyRules.DesignationMax),
        _ => text[key],
    };
}
