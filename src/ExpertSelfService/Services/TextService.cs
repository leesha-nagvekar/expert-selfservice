using System.Globalization;
using System.Text.Json;

namespace ExpertSelfService.Services;

/// <summary>All text the app shows, from i18n/da.json and i18n/en.json.</summary>
public sealed class TextService
{
    private const string DefaultLanguage = "da";
    private static readonly string[] Languages = [DefaultLanguage, "en"];

    private readonly Dictionary<string, Dictionary<string, string>> _texts =
        Languages.ToDictionary(language => language, Load);

    /// <summary>The text in the current language. A missing key throws KeyNotFoundException.</summary>
    public string this[string key]
    {
        get
        {
            var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var texts = _texts.GetValueOrDefault(language) ?? _texts[DefaultLanguage];
            return texts[key];
        }
    }

    private static Dictionary<string, string> Load(string language)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "i18n", $"{language}.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{path} is empty");
    }
}
