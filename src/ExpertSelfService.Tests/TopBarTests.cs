using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExpertSelfService.Tests;

public class TopBarTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> FrontPage()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Front_page_is_in_danish_with_real_danish_letters()
    {
        var html = await FrontPage();

        Assert.Contains("""<html lang="da">""", html);
        Assert.Contains("Selvbetjening for skønsmænd", html); // raw ø and æ, not &#xF8;
        Assert.Contains("Her kan du som skønsmand hos VBA", html);
    }

    [Fact]
    public async Task Top_bar_shows_logo_and_language_switch()
    {
        var html = await FrontPage();

        Assert.Contains("<header class=\"topbar\">", html);
        Assert.Contains("img/vn-logo-white.png", html);
        Assert.Contains("Voldgiftsnævnet for Bygge- og Anlægsvirksomhed – til forsiden", html);
        Assert.Contains(">Dansk<", html);
        Assert.Contains(">English<", html);
    }

    [Fact]
    public async Task Narrow_screens_get_the_mark_instead_of_the_full_logo()
    {
        var html = await FrontPage();

        Assert.Matches("""<source media="\(max-width: 479px\)" srcset="[^"]*img/vn-mark-white\.png">""", html);
    }

    [Fact]
    public async Task On_a_phone_the_language_switch_says_da_and_en()
    {
        var html = await FrontPage();

        Assert.Contains("""<span class="label-short">DA</span>""", html);
        Assert.Contains("""<span class="label-short">EN</span>""", html);
        Assert.Contains("""<span class="label-long">Dansk</span>""", html);
        Assert.Contains("""<span class="label-long">English</span>""", html);
    }

    [Fact]
    public void The_top_bar_stays_at_the_top_and_nothing_scrolls_behind_it()
    {
        var css = RepoFiles.Read("src", "ExpertSelfService", "wwwroot", "app.css");

        var topbar = Regex.Match(css, @"(?m)^\.topbar \{[^}]*\}").Value;
        Assert.Contains("position: sticky;", topbar);
        Assert.Contains("top: 0;", topbar);
        Assert.Contains("z-index:", topbar);

        // Scrolling to a field, an error or the reject box stops below the bar.
        Assert.Matches(@"(?m)^html \{[^}]*scroll-padding-top:", css);
    }

    [Fact]
    public void Phones_under_600px_get_the_one_line_bar()
    {
        var css = RepoFiles.Read("src", "ExpertSelfService", "wwwroot", "app.css");

        var phone = css.IndexOf("@media (max-width: 599px)", StringComparison.Ordinal);
        Assert.True(phone >= 0, "No phone rules for the top bar");
        Assert.Contains(".label-short", css[phone..]);
    }

    [Theory]
    [InlineData("tokens.css")]
    [InlineData("fonts.css")]
    [InlineData("app.css")]
    public async Task Stylesheets_are_linked_and_load(string file)
    {
        var html = await FrontPage();
        Assert.Contains($"""<link rel="stylesheet" href="/{file}">""", html);

        var response = await _client.GetAsync("/" + file);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("img/vn-logo-white.png")]
    [InlineData("img/vn-mark-white.png")]
    public async Task Logos_load(string file)
    {
        var html = await FrontPage();
        Assert.Contains($"/{file}", html);

        var response = await _client.GetAsync("/" + file);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("da")]
    [InlineData("en")]
    public void Every_text_key_used_in_a_view_exists(string language)
    {
        var keys = JsonSerializer
            .Deserialize<Dictionary<string, string>>(RepoFiles.Read("i18n", $"{language}.json"))!
            .Keys;

        var used = Directory
            .EnumerateFiles(Path.Combine(RepoFiles.App, "Views"), "*.cshtml", SearchOption.AllDirectories)
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), """Text\["([^"]+)"\]"""))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(used);
        Assert.Empty(used.Except(keys));
    }
}
