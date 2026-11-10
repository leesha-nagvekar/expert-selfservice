using System.Globalization;

namespace ExpertSelfService.Services;

/// <summary>
/// Dates from VBA's columns (Created, UntilDate) are stored as 'yyyy-MM-dd' and shown in the
/// short date of the chosen language: 31.03.2027 in Danish, 31/03/2027 in English (en-GB).
/// </summary>
public static class LocalDate
{
    public const string StoredFormat = "yyyy-MM-dd";

    /// <summary>The formatted date, the stored text unchanged if it isn't a date, or null when empty.</summary>
    public static string? Format(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        return DateOnly.TryParseExact(stored, StoredFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("d", CultureInfo.CurrentCulture)
            : stored;
    }
}
