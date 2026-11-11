namespace ExpertSelfService.Models;

/// <summary>
/// X5 as typed. The page shows these values unchanged, so nothing typed is lost on an error.
/// The trimmed versions are what gets checked and saved.
/// </summary>
public sealed record AddressForm(string Street, string Postcode, string PhoneMobile, string PhoneWork, string PhoneHome)
{
    public static AddressForm From(string? street, string? postcode, string? phoneMobile, string? phoneWork, string? phoneHome) =>
        new(street ?? "", postcode ?? "", phoneMobile ?? "", phoneWork ?? "", phoneHome ?? "");

    /// <summary>Trimmed; an empty phone is null, so the column is cleared rather than set to "".</summary>
    public static string? Clean(string value) => value.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}

/// <summary>X5: the form, the city for the postcode, and the error next to each field (keyed by field name).</summary>
public sealed record AddressPage(AddressForm Form, string? City, IReadOnlyDictionary<string, string> Errors)
{
    public string? Error(string field) => Errors.GetValueOrDefault(field);
}
