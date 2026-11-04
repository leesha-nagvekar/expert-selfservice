using System.Text.RegularExpressions;

namespace ExpertSelfService.Tests;

public class ColourTests
{
    private static readonly string[] AppCssPath = ["src", "ExpertSelfService", "wwwroot", "app.css"];
    private static readonly string[] TokensPath = ["src", "ExpertSelfService", "wwwroot", "tokens.css"];

    private static (Dictionary<string, string> Light, Dictionary<string, string> Dark) Tokens()
    {
        var css = RepoFiles.Read(TokensPath);
        var media = css.IndexOf("@media (prefers-color-scheme: dark)", StringComparison.Ordinal);
        return media < 0
            ? (Parse(css), [])
            : (Parse(css[..media]), Parse(css[media..]));
    }

    private static Dictionary<string, string> Parse(string css) =>
        Regex.Matches(css, @"(--[a-z-]+):\s*(#[0-9A-Fa-f]+)")
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);

    [Fact]
    public void Tokens_match_guide_section_5_in_light_and_dark()
    {
        var rows = Regex.Matches(
            RepoFiles.Read("docs", "guide.md"),
            @"^\| `(--[a-z-]+)`\s*\| `(#[0-9A-Fa-f]+)`\s*\| `(#[0-9A-Fa-f]+)`",
            RegexOptions.Multiline);
        Assert.Equal(15, rows.Count);

        var (light, dark) = Tokens();
        Assert.All(rows, row =>
        {
            var token = row.Groups[1].Value;
            Assert.Equal(row.Groups[2].Value, light.GetValueOrDefault(token));
            Assert.Equal(row.Groups[3].Value, dark.GetValueOrDefault(token));
        });
    }

    [Fact]
    public void Every_light_token_also_has_a_dark_value()
    {
        var (light, dark) = Tokens();

        Assert.Equal(light.Keys.Order(), dark.Keys.Order());
    }

    [Fact]
    public void Top_bar_text_is_white_in_both_themes()
    {
        var (light, dark) = Tokens();

        Assert.Equal("#FFFFFF", light.GetValueOrDefault("--on-topbar"));
        Assert.Equal("#FFFFFF", dark.GetValueOrDefault("--on-topbar"));
    }

    [Fact]
    public void Top_bar_uses_on_topbar_because_on_brand_is_dark_in_dark_mode()
    {
        var topbarRules = Regex.Matches(RepoFiles.Read(AppCssPath), @"\.topbar[^{]*\{[^}]*\}")
            .Select(match => match.Value)
            .ToList();

        Assert.NotEmpty(topbarRules);
        Assert.DoesNotContain(topbarRules, rule => rule.Contains("--on-brand"));
        Assert.Contains(topbarRules, rule => rule.Contains("color: var(--on-topbar)"));
    }

    [Theory]
    [InlineData("color: #fff;")]
    [InlineData("color: #00437C;")]
    [InlineData("background: rgb(0, 0, 0);")]
    [InlineData("background: hsla(0, 0%, 0%, 0.5);")]
    [InlineData("color: white;")]
    [InlineData("""<p style="border: 1px solid red">""")]
    public void Colour_scan_catches_a_planted_colour(string text)
    {
        Assert.NotEmpty(ColourCodes(text));
    }

    [Theory]
    [InlineData("color: var(--text);")]
    [InlineData("""<a href="#main">""")]
    [InlineData("<p>The white logo on a blue bar</p>")]
    public void Colour_scan_leaves_normal_text_alone(string text)
    {
        Assert.Empty(ColourCodes(text));
    }

    [Fact]
    public void No_colours_outside_tokens_css()
    {
        var extensions = new[] { ".css", ".cshtml", ".cs", ".js" };
        var files = Directory.EnumerateFiles(RepoFiles.App, "*", SearchOption.AllDirectories)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Where(path => Path.GetFileName(path) != "tokens.css")
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Intersect(["bin", "obj"]).Any())
            .ToList();
        Assert.NotEmpty(files);

        var offenders = files.SelectMany(path =>
            ColourCodes(File.ReadAllText(path)).Select(colour => $"{Path.GetFileName(path)}: {colour}"));

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> ColourCodes(string text)
    {
        const string Code = @"#[0-9a-fA-F]{3,8}\b|\b(?:rgb|rgba|hsl|hsla)\(";
        const string Named =
            @"\b(?:color|background|background-color|border|border-color|outline|outline-color|fill|stroke)\s*:"
            + @"[^;}""]*\b(?:white|black|red|green|blue|orange|yellow|gray|grey|purple|pink|brown)\b";

        return Regex.Matches(text, $"{Code}|{Named}").Select(match => match.Value);
    }
}
