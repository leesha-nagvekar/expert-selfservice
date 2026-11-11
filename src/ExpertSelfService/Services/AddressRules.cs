namespace ExpertSelfService.Services;

/// <summary>
/// SPEC X5. The limits and the Danish regions are the C# copy of rules.py (MAX_LENGTHS, DANISH_REGION_IDS);
/// AddressTests reads rules.py, so the two can't drift apart unnoticed.
/// </summary>
public static class AddressRules
{
    public const int StreetMax = 50;
    public const int PhoneMax = 50;
    public const int PhoneMinDigits = 8;
    public const int PhoneMaxDigits = 15;

    /// <summary>Regions.Id of the five Danish regions. A postcode outside them is unknown.</summary>
    public static readonly IReadOnlyList<int> DanishRegionIds = [4, 5, 6, 7, 8];

    public static bool IsPostcodeFormat(string postcode) =>
        postcode.Length == 4 && postcode.All(char.IsAsciiDigit);

    /// <summary>Digits and spaces, with one optional + at the start; 8-15 digits; at most 50 characters. Expects a trimmed value.</summary>
    public static bool IsValidPhone(string phone)
    {
        if (phone.Length > PhoneMax)
        {
            return false;
        }

        var rest = phone.StartsWith('+') ? phone[1..] : phone;
        if (!rest.All(ch => char.IsAsciiDigit(ch) || ch == ' '))
        {
            return false;
        }

        var digits = rest.Count(char.IsAsciiDigit);
        return digits is >= PhoneMinDigits and <= PhoneMaxDigits;
    }
}
