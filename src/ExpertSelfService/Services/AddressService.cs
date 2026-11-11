using Dapper;
using ExpertSelfService.Models;
using Microsoft.Data.Sqlite;

namespace ExpertSelfService.Services;

/// <summary>A Danish postcode from Zipcodes, with its city.</summary>
public sealed record Postcode(long Id, string Code, string City)
{
    /// <summary>How the change log shows a postcode: "8000 Aarhus C".</summary>
    public string Text => $"{Code} {City}";
}

public enum SaveResult
{
    Saved,
    NothingChanged,
    ExpertNotFound,
}

/// <summary>Story E3, screen X5: address and phone, saved at once (SPEC section 3).</summary>
public sealed class AddressService(Database database, ExpertChanges changes)
{
    private sealed record CurrentRow(
        string? Street, long? ZipcodeId, string? Zipcode, string? City,
        string? PhoneMobile, string? PhoneWork, string? PhoneHome);

    /// <summary>The expert's current values for the form, or null when the expert is gone or Retired.</summary>
    public AddressPage? Load(long expertId)
    {
        using var db = database.Open();
        var row = Current(db, null, expertId);
        return row is null
            ? null
            : new AddressPage(
                AddressForm.From(row.Street, row.Zipcode, row.PhoneMobile, row.PhoneWork, row.PhoneHome),
                row.City,
                new Dictionary<string, string>());
    }

    /// <summary>The Danish postcode with these 4 digits, or null. Only the regions in AddressRules.DanishRegionIds count.</summary>
    public Postcode? FindPostcode(string code)
    {
        var trimmed = code.Trim();
        if (!AddressRules.IsPostcodeFormat(trimmed))
        {
            return null;
        }

        using var db = database.Open();
        return db.QueryFirstOrDefault<Postcode>(
            """
            SELECT z.Id, z.Zipcode AS Code, ci.City
            FROM Zipcodes z
            JOIN Cities ci ON ci.Id = z.CityId
            WHERE z.Zipcode = @code AND ci.RegionId IN @regions
            ORDER BY z.Id
            LIMIT 1
            """,
            new { code = trimmed, regions = AddressRules.DanishRegionIds });
    }

    /// <summary>The i18n key of the error for each wrong field. Every field is checked (decisions.md, E3).</summary>
    public Dictionary<string, string> Validate(AddressForm form, out Postcode? postcode)
    {
        var errors = new Dictionary<string, string>();

        var street = form.Street.Trim();
        if (street.Length == 0)
        {
            errors["Street"] = "address.error.street_required";
        }
        else if (street.Length > AddressRules.StreetMax)
        {
            errors["Street"] = "address.error.street_too_long";
        }

        postcode = null;
        if (!AddressRules.IsPostcodeFormat(form.Postcode.Trim()))
        {
            errors["Postcode"] = "address.error.postcode_format";
        }
        else if ((postcode = FindPostcode(form.Postcode)) is null)
        {
            errors["Postcode"] = "address.error.postcode_unknown";
        }

        foreach (var (field, value) in Phones(form))
        {
            if (AddressForm.Clean(value) is string phone && !AddressRules.IsValidPhone(phone))
            {
                errors[field] = "address.error.phone";
            }
        }

        return errors;
    }

    /// <summary>
    /// Saves a valid form. Candidates, LastUpdated and one change-log row per changed field are written
    /// in one transaction; nothing is written when nothing changed.
    /// </summary>
    public SaveResult Save(long expertId, AddressForm form, Postcode postcode)
    {
        using var db = database.Open();
        using var transaction = db.BeginTransaction();

        var current = Current(db, transaction, expertId);
        if (current is null)
        {
            return SaveResult.ExpertNotFound;
        }

        var street = form.Street.Trim();
        var phones = Phones(form).ToDictionary(p => p.Field, p => AddressForm.Clean(p.Value));

        var changed = new List<FieldChange>();
        AddIfChanged(changed, "Street", current.Street, street);
        if (current.ZipcodeId != postcode.Id)
        {
            var old = current.Zipcode is null ? current.ZipcodeId?.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{current.Zipcode} {current.City}".Trim();
            changed.Add(new FieldChange("ZipcodeId", old, postcode.Text));
        }

        AddIfChanged(changed, "PhoneMobile", current.PhoneMobile, phones["PhoneMobile"]);
        AddIfChanged(changed, "PhoneWork", current.PhoneWork, phones["PhoneWork"]);
        AddIfChanged(changed, "PhoneHome", current.PhoneHome, phones["PhoneHome"]);

        if (changed.Count == 0)
        {
            return SaveResult.NothingChanged;
        }

        db.Execute(
            """
            UPDATE Candidates
            SET Street = @street, ZipcodeId = @zipcodeId, PhoneMobile = @mobile, PhoneWork = @work, PhoneHome = @home
            WHERE Id = @expertId
            """,
            new
            {
                street,
                zipcodeId = postcode.Id,
                mobile = phones["PhoneMobile"],
                work = phones["PhoneWork"],
                home = phones["PhoneHome"],
                expertId,
            },
            transaction);
        changes.Record(db, transaction, expertId, ExpertChanges.ByExpert, expertId, changed);

        transaction.Commit();
        return SaveResult.Saved;
    }

    private static IEnumerable<(string Field, string Value)> Phones(AddressForm form) =>
        [("PhoneMobile", form.PhoneMobile), ("PhoneWork", form.PhoneWork), ("PhoneHome", form.PhoneHome)];

    /// <summary>Empty and missing count as the same value, so clearing an already empty phone is no change.</summary>
    private static void AddIfChanged(List<FieldChange> changed, string field, string? oldValue, string? newValue)
    {
        var old = string.IsNullOrEmpty(oldValue) ? null : oldValue;
        var now = string.IsNullOrEmpty(newValue) ? null : newValue;
        if (!string.Equals(old, now, StringComparison.Ordinal))
        {
            changed.Add(new FieldChange(field, old, now));
        }
    }

    private static CurrentRow? Current(SqliteConnection db, SqliteTransaction? transaction, long expertId) =>
        db.QuerySingleOrDefault<CurrentRow>(
            """
            SELECT c.Street, c.ZipcodeId, z.Zipcode, ci.City, c.PhoneMobile, c.PhoneWork, c.PhoneHome
            FROM Candidates c
            LEFT JOIN Zipcodes z ON z.Id = c.ZipcodeId
            LEFT JOIN Cities ci ON ci.Id = z.CityId
            WHERE c.Id = @expertId AND c.Status <> @retired
            """,
            new { expertId, retired = ExpertStatus.Retired },
            transaction);
}
