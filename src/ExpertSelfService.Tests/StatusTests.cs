using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>
/// The status codes and names live in rules.py (Python) and in the app (C#).
/// These tests read rules.py, so a change in one language that the other doesn't follow fails here.
/// decisions.md, "Data rules in two languages".
/// </summary>
public sealed class StatusTests : IDisposable
{
    private const int Retired = 0;

    private readonly AppFactory _app = new();

    public void Dispose() => _app.Dispose();

    /// <summary>(code, English name) for every entry in rules.STATUS_NAMES.</summary>
    public static TheoryData<int, string> StatusesInRulesPy()
    {
        var rules = RepoFiles.Read("rules.py");
        var names = Regex.Match(rules, @"STATUS_NAMES = \{(.*?)\}", RegexOptions.Singleline).Groups[1].Value;
        var data = new TheoryData<int, string>();
        foreach (Match entry in Regex.Matches(names, @"(\w+): ""([^""]+)"""))
        {
            var constant = Regex.Match(rules, $@"^{entry.Groups[1].Value} = (-?\d+)\s*$", RegexOptions.Multiline);
            data.Add(int.Parse(constant.Groups[1].Value, CultureInfo.InvariantCulture), entry.Groups[2].Value);
        }

        return data;
    }

    [Fact]
    public void Rules_py_has_the_six_statuses()
    {
        Assert.Equal(6, StatusesInRulesPy().Count);
    }

    private void SetAnnasStatus(int status)
    {
        using var db = _app.Open();
        db.Execute(
            "UPDATE Candidates SET Status = @status, UntilDate = '2027-03-31' WHERE Id = 1", new { status });
    }

    private static string VbaSection(string html) =>
        Regex.Match(html, """<section[^>]*\bid="vba"[^>]*>(.*?)</section>""", RegexOptions.Singleline).Groups[1].Value;

    [Theory]
    [MemberData(nameof(StatusesInRulesPy))]
    public async Task Each_status_in_rules_py_is_shown_with_its_english_name(int code, string name)
    {
        var browser = new Browser(_app);
        await browser.UseEnglish();

        if (code == Retired)
        {
            // Retired can't log in, and is logged out if set during a session.
            await browser.LogIn(Anna);
            SetAnnasStatus(code);
            AssertRedirect(await browser.Get("/profile"), "/");
            return;
        }

        SetAnnasStatus(code);
        await browser.LogIn(Anna);

        Assert.Contains(name, VbaSection(await browser.Html("/profile")));
    }

    // VBA's own words, confirmed by Leesha on 10 Nov 2026.
    [Theory]
    [InlineData(-1, "Standby")]
    [InlineData(1, "Aktiv")]
    [InlineData(2, "Optag")]
    [InlineData(3, "Observation")]
    [InlineData(4, "Evalueret")]
    public async Task Each_status_has_its_danish_name(int code, string name)
    {
        SetAnnasStatus(code);
        var browser = new Browser(_app); // Danish is the default
        await browser.LogIn(Anna);

        Assert.Contains(name, VbaSection(await browser.Html("/profile")));
    }

    [Fact]
    public async Task An_unknown_status_is_shown_with_its_number()
    {
        SetAnnasStatus(7);
        var browser = new Browser(_app);
        await browser.UseEnglish();
        await browser.LogIn(Anna);

        Assert.Contains("Unknown (7)", VbaSection(await browser.Html("/profile")));
    }

    [Fact]
    public async Task An_unknown_status_also_has_an_icon_and_its_own_colour()
    {
        SetAnnasStatus(7);
        var browser = new Browser(_app);
        await browser.UseEnglish();
        await browser.LogIn(Anna);

        var vba = Regex.Replace(VbaSection(await browser.Html("/profile")), @"\s+", " ");
        Assert.Matches("""class="status status-unknown">\s*<svg[^>]*aria-hidden="true"[^>]*>.*?</svg>\s*Unknown \(7\)""", vba);
    }
}
