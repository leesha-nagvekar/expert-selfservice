using System.Globalization;
using ExpertSelfService.Models;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>Story E3, screen X5: edit address and phone. Saved at once (SPEC section 3).</summary>
[Authorize(Policy = SessionClaims.ExpertPolicy)]
public class AddressController(AddressService addresses, TextService text) : Controller
{
    private const string EditView = "Edit";

    // No id in the route, the query or the form: the expert is always the one in the session (SPEC section 2).
    [HttpGet("/profile/address")]
    public IActionResult Edit()
    {
        var page = SessionClaims.ExpertIdOf(User) is long expertId ? addresses.Load(expertId) : null;
        return page is null ? NotFound() : View(EditView, page);
    }

    // Only these five fields are read. Any other field in the post, such as an id, is ignored.
    [HttpPost("/profile/address")]
    public IActionResult Save(string? street, string? postcode, string? phoneMobile, string? phoneWork, string? phoneHome, string? action)
    {
        if (SessionClaims.ExpertIdOf(User) is not long expertId)
        {
            return NotFound();
        }

        var form = AddressForm.From(street, postcode, phoneMobile, phoneWork, phoneHome);

        // "Find city", the fallback without JavaScript: show the city, save nothing.
        if (action == "find")
        {
            var found = addresses.FindPostcode(form.Postcode);
            var errors = new Dictionary<string, string>();
            if (found is null)
            {
                errors["Postcode"] = Message(AddressRules.IsPostcodeFormat(form.Postcode.Trim())
                    ? "address.error.postcode_unknown"
                    : "address.error.postcode_format");
            }

            return View(EditView, new AddressPage(form, found?.City, errors));
        }

        var errorKeys = addresses.Validate(form, out var valid);
        if (errorKeys.Count > 0 || valid is null)
        {
            var messages = errorKeys.ToDictionary(e => e.Key, e => Message(e.Value));
            return View(EditView, new AddressPage(form, valid?.City, messages));
        }

        switch (addresses.Save(expertId, form, valid))
        {
            case SaveResult.ExpertNotFound:
                return NotFound();
            case SaveResult.Saved:
                TempData[ProfilePage.SavedSectionKey] = "address";
                break;
        }

        return Redirect("/profile");
    }

    /// <summary>For postcode.js: the city of a Danish postcode, or 404.</summary>
    [HttpGet("/postcode/{code}")]
    public IActionResult City(string code) =>
        addresses.FindPostcode(code) is Postcode found ? Json(new { city = found.City }) : NotFound();

    private string Message(string key) => key == "address.error.street_too_long"
        ? string.Format(CultureInfo.CurrentCulture, text[key], AddressRules.StreetMax)
        : text[key];
}
