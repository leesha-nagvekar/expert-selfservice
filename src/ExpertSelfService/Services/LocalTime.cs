using System.Globalization;

namespace ExpertSelfService.Services;

/// <summary>Times are stored in UTC and shown in Danish time, formatted from the chosen language (SPEC section 5).</summary>
public static class LocalTime
{
    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

    public static string Format(string utcText)
    {
        var local = TimeZoneInfo.ConvertTime(Database.FromText(utcText), Copenhagen);
        return local.ToString("g", CultureInfo.CurrentCulture);
    }

    /// <summary>The date in Denmark at this moment, as Candidates stores dates (yyyy-MM-dd), for LastUpdated.</summary>
    public static string DateText(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, Copenhagen).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
