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
}
