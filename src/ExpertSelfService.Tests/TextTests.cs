using System.Globalization;
using System.Text.Json;
using ExpertSelfService.Services;

namespace ExpertSelfService.Tests;

public class TextTests
{
    private static Dictionary<string, string> Load(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(RepoFiles.Read("i18n", $"{language}.json"))!;

    private static string InCulture(string culture, Func<string> read)
    {
        var before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            return read();
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    [Fact]
    public void Both_languages_have_the_same_keys()
    {
        Assert.Equal(Load("da").Keys.Order(), Load("en").Keys.Order());
    }

    [Theory]
    [InlineData("da")]
    [InlineData("en")]
    public void No_text_is_empty(string language)
    {
        Assert.DoesNotContain(Load(language), pair => string.IsNullOrWhiteSpace(pair.Value));
    }

    [Theory]
    [InlineData("da", "Selvbetjening for skønsmænd")]
    [InlineData("en", "Expert self-service")]
    public void Text_comes_in_the_current_language(string culture, string expected)
    {
        var text = new TextService();

        Assert.Equal(expected, InCulture(culture, () => text["home.title"]));
    }

    [Fact]
    public void Any_other_language_falls_back_to_danish()
    {
        var text = new TextService();

        Assert.Equal("Selvbetjening for skønsmænd", InCulture("de", () => text["home.title"]));
    }

    [Fact]
    public void A_missing_key_throws_instead_of_showing_nothing()
    {
        var text = new TextService();

        Assert.Throws<KeyNotFoundException>(() => text["no.such.key"]);
    }
}
