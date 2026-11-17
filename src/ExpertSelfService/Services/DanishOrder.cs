using System.Globalization;

namespace ExpertSelfService.Services;

/// <summary>
/// Danish alphabetical order (Æ, Ø, Å last), whatever the page's language. Used where a list must have one fixed
/// order: the stored list of fields of expertise (X9) and the secretariat's search (S-A).
/// </summary>
public static class DanishOrder
{
    public static readonly StringComparer Comparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("da-DK"), ignoreCase: true);
}
