using System.Net;
using Dapper;
using ExpertSelfService.Services;
using Microsoft.Extensions.DependencyInjection;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>
/// Anna (expert 1) as a dishonest expert, trying to read or change another expert's data (Phase 6, 24 Nov).
/// These are the attacks from that list that had no test yet; the letters are the list's (C2, C3, C5, C6, D2,
/// E1, E3). The others are covered where the feature is tested (ProfileTests, NameTests, LoginTests, ...).
/// </summary>
public sealed class DishonestExpertTests : IAsyncLifetime, IDisposable
{
    private const string WrongOrExpired = "The code is wrong or has expired.";
    private const string CantBeUsed = "This email address can't be used. Choose another.";
    private const string LoginCookie = ".AspNetCore.Cookies";
    private const string TempDataCookie = ".AspNetCore.Mvc.CookieTempDataProvider";

    // Markup Anna types into her own data, hoping it runs when the secretariat opens her page.
    private const string ImgTag = "<img src=x onerror=alert(1)>";
    private const string ScriptTag = "<script>alert('company')</script>";
    private const string SvgTag = "<svg onload=alert(2)>";

    private readonly AppFactory _app = new();
    private readonly Browser _anna;

    public DishonestExpertTests() => _anna = new Browser(_app);

    public Task InitializeAsync() => _anna.UseEnglish();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _app.Dispose();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Browser> LoggedIn(string email, string landing)
    {
        var browser = new Browser(_app);
        await browser.UseEnglish();
        AssertRedirect(await browser.LogIn(email), landing);
        return browser;
    }

    private int FailedTries(string email) =>
        _app.Count("SELECT COALESCE(SUM(failed_count), 0) FROM ss_login_lockouts WHERE email = @email", new { email });

    private string EmailOf(long id)
    {
        using var db = _app.Open();
        return db.QuerySingle<string>("SELECT Email FROM Candidates WHERE Id = @id", new { id });
    }

    /// <summary>The code in the newest mail to this address, whatever its letter case.</summary>
    private string CodeSentTo(string email)
    {
        using var db = _app.Open();
        var body = db.QuerySingle<string>(
            "SELECT body FROM ss_dev_mailbox WHERE lower(to_email) = lower(@email) ORDER BY id DESC LIMIT 1", new { email });
        return System.Text.RegularExpressions.Regex.Match(body, @"\b[0-9]{6}\b").Value;
    }

    /// <summary>Changes one character in the middle of a cookie's value, as someone editing it by hand would.</summary>
    private static void Edit(Browser browser, string name)
    {
        var cookie = browser.Cookies.GetAllCookies().Single(c => c.Name == name);
        var middle = cookie.Value.Length / 2;
        var swapped = cookie.Value[middle] == 'A' ? 'B' : 'A';
        cookie.Value = string.Concat(cookie.Value.AsSpan(0, middle), swapped.ToString(), cookie.Value.AsSpan(middle + 1));
    }

    /// <summary>The page shows the text (once decoded), but never the raw tag, so the browser can't run it.</summary>
    private static void AssertShownAsText(string html, string markup)
    {
        Assert.DoesNotContain(markup, html);
        Assert.Contains(markup, WebUtility.HtmlDecode(html));
    }

    // -------------------------------------------------------------------------
    // C: logging in as Bo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Guesses_sent_at_the_same_time_cant_get_past_the_lock()
    {
        // One level below HTTP: through the web pages each guess is so quick, and SQLite's retry pauses so long,
        // that 20 guesses never really overlap. Here 20 threads call the check at the same instant.
        var login = _app.Services.GetRequiredService<LoginService>();
        Assert.Null(login.RequestCode(Bo));
        var right = _app.LatestCode(Bo);
        var wrong = Enumerable.Range(0, 1_000_000).Select(n => n.ToString("D6")).Where(c => c != right).Take(20).ToList();

        using var start = new Barrier(wrong.Count);
        var results = await Task.WhenAll(wrong.Select(code => Task.Factory.StartNew(
            () =>
            {
                start.SignalAndWait();
                return login.CheckCode(Bo, code);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning, // its own thread, so all 20 can wait at the barrier at once
            TaskScheduler.Default)));

        Assert.All(results, result => Assert.Null(result.Person));
        Assert.Equal(OneTimeCodes.MaxFailedTries, FailedTries(Bo));
        Assert.Equal(1, _app.Count("SELECT COUNT(*) FROM ss_login_lockouts WHERE email = @Bo AND locked_until IS NOT NULL", new { Bo }));

        // Locked: even the right code is refused now.
        var last = login.CheckCode(Bo, right);
        Assert.Null(last.Person);
        Assert.NotNull(last.LockedFor);
    }

    [Fact]
    public async Task Annas_own_code_cant_log_her_in_as_bo()
    {
        // Two tabs: in the first the email in progress is Bo's; in the second she asks for her own code,
        // so hers is the newest code of all when she types it in the first tab.
        var ownTab = new Browser(_app);
        await ownTab.UseEnglish();
        await _anna.RequestCode(Bo);
        await ownTab.RequestCode(Anna);
        var annasCode = _app.LatestCode(Anna);

        var response = await _anna.SubmitCode(annasCode);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(WrongOrExpired, await Body(response));
        Assert.Equal(1, FailedTries(Bo));
        AssertRedirect(await _anna.Get("/profile"), "/");
        AssertRedirect(await _anna.Get("/staff"), "/");

        // The code was real: it still logs her in as herself.
        AssertRedirect(await ownTab.SubmitCode(annasCode), "/profile");
    }

    [Fact]
    public async Task An_edited_login_cookie_counts_as_not_logged_in()
    {
        AssertRedirect(await _anna.LogIn(Anna), "/profile");
        Assert.Equal(HttpStatusCode.OK, (await _anna.Get("/profile")).StatusCode);

        Edit(_anna, LoginCookie);

        AssertRedirect(await _anna.Get("/profile"), "/");
        AssertRedirect(await _anna.Get("/profile/address"), "/");
        AssertRedirect(await _anna.Get("/staff"), "/");
        Assert.DoesNotContain("Vestergade 8", await _anna.Html("/"));
    }

    [Fact]
    public async Task An_edited_login_email_cookie_starts_over()
    {
        await _anna.RequestCode(Anna);
        var code = _app.LatestCode(Anna);

        // She edits the cookie that holds the email in progress, hoping to turn it into Bo's.
        Edit(_anna, TempDataCookie);

        AssertRedirect(await _anna.Get("/login/code"), "/");
        AssertRedirect(await _anna.SubmitCode(code, tokenPage: "/"), "/");
        AssertRedirect(await _anna.Get("/profile"), "/");
        Assert.Equal(0, FailedTries(Bo));
    }

    // -------------------------------------------------------------------------
    // D: taking Bo's email
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Bos_address_in_capitals_cant_be_taken_either()
    {
        var anna = await LoggedIn(Anna, "/profile");

        AssertRedirect(await anna.Post("/profile/email", "/profile/email", ("NewEmail", "  BO.DEMO@EXAMPLE.COM ")), "/profile/email/code");
        // Only someone with Bo's inbox has this code; the test gives it to her to show even that isn't enough.
        var response = await anna.Post("/profile/email/code", "/profile/email/code", ("Code", CodeSentTo(Bo)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(CantBeUsed, WebUtility.HtmlDecode(await Body(response)));
        Assert.Equal(Anna, EmailOf(1));
        Assert.Equal(Bo, EmailOf(2));
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_change_log WHERE field = 'Email'"));
    }

    // -------------------------------------------------------------------------
    // E: getting the secretariat to do it
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Markup_in_a_name_reaches_s_b_and_s_c_as_text()
    {
        var anna = await LoggedIn(Anna, "/profile");
        AssertRedirect(await anna.Post("/profile/name", "/profile/name",
            ("FirstName", ImgTag), ("MiddleName", ""), ("LastName", "Demo")), "/profile");

        var sofie = await LoggedIn(Sofie, "/staff");

        AssertShownAsText(await sofie.Html("/staff/pending"), ImgTag);
        AssertShownAsText(await sofie.Html("/staff/experts/1"), ImgTag);
    }

    [Fact]
    public async Task Markup_in_company_and_street_reaches_s_c_as_text()
    {
        var anna = await LoggedIn(Anna, "/profile");
        AssertRedirect(await anna.Post("/profile/company", "/profile/company",
            ("Company", ScriptTag), ("Cvr", "12345674"), ("Designation", ImgTag)), "/profile");
        AssertRedirect(await anna.Post("/profile/address", "/profile/address",
            ("Street", SvgTag), ("Postcode", "2100"), ("PhoneMobile", "+45 20 11 22 33"),
            ("PhoneWork", "33 44 55 66"), ("PhoneHome", "")), "/profile");

        var sofie = await LoggedIn(Sofie, "/staff");
        var html = await sofie.Html("/staff/experts/1");

        // In the details and in the change log, which both show the new values.
        AssertShownAsText(html, ScriptTag);
        AssertShownAsText(html, ImgTag);
        AssertShownAsText(html, SvgTag);
    }

    [Fact]
    public async Task An_approved_name_with_markup_reaches_s_a_as_text()
    {
        var anna = await LoggedIn(Anna, "/profile");
        AssertRedirect(await anna.Post("/profile/name", "/profile/name",
            ("FirstName", ImgTag), ("MiddleName", ""), ("LastName", "Demo")), "/profile");
        var changeId = _app.Count("SELECT id FROM ss_pending_changes WHERE candidate_id = 1 AND field = 'FirstName'");

        var sofie = await LoggedIn(Sofie, "/staff");
        AssertRedirect(await sofie.Post($"/staff/pending/{changeId}/approve", "/staff/pending"), "/staff/pending");

        AssertShownAsText(await sofie.Html("/staff?q=Demo"), ImgTag);
        AssertShownAsText(await sofie.Html("/staff/experts/1"), ImgTag);
    }

    [Fact]
    public async Task Annas_token_doesnt_work_in_bos_browser()
    {
        var anna = await LoggedIn(Anna, "/profile");
        var annasToken = await anna.TokenFrom("/profile/address");

        // Bo has opened his own address page once, so his browser has its own antiforgery cookie.
        var bo = await LoggedIn(Bo, "/profile");
        await bo.Html("/profile/address");

        // Anna's page makes Bo's browser post her token to his address form.
        var response = await bo.Http.PostAsync("/profile/address", new FormUrlEncodedContent(
        [
            KeyValuePair.Create("Street", "Angrebsvej 1"),
            KeyValuePair.Create("Postcode", "2100"),
            KeyValuePair.Create("PhoneMobile", ""),
            KeyValuePair.Create("PhoneWork", ""),
            KeyValuePair.Create("PhoneHome", ""),
            KeyValuePair.Create("__RequestVerificationToken", annasToken),
        ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Vestergade 8", StreetOf(2));
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_change_log WHERE candidate_id = 2"));
    }

    private string StreetOf(long id)
    {
        using var db = _app.Open();
        return db.QuerySingle<string>("SELECT Street FROM Candidates WHERE Id = @id", new { id });
    }
}
