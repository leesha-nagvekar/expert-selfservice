using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>Story E1, screens X1 (email) and X2 (code). SPEC section 4.</summary>
public sealed class LoginTests : IAsyncLifetime, IDisposable
{
    private const string Sent = "If the email is registered, a code has been sent";
    private const string EmailRequired = "Enter your email address";
    private const string EmailInvalid = "Enter a valid email address.";
    private const string MustBeSixDigits = "The code must be 6 digits";
    private const string WrongOrExpired = "The code is wrong or has expired";
    private const string LockedFifteen = "Too many attempts. Try again in 15 minutes.";

    // A new app and database per test, so no test sees another test's codes or clock.
    private readonly AppFactory _app = new();
    private readonly Browser _browser;

    public LoginTests() => _browser = new Browser(_app);

    public Task InitializeAsync() => _browser.UseEnglish();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _app.Dispose();

    private int Codes(string email) =>
        _app.Count("SELECT COUNT(*) FROM ss_login_codes WHERE email = @email", new { email });

    private int Messages(string email) =>
        _app.Count("SELECT COUNT(*) FROM ss_dev_mailbox WHERE to_email = @email", new { email });

    private static string WithoutTokens(string html) =>
        Regex.Replace(html, """(<input[^>]*name="__RequestVerificationToken"[^>]*value=")[^"]*""", "$1");

    private async Task WrongCodes(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await _browser.SubmitCode(WrongCodeFor(_app.LatestCodeOrNull()));
        }
    }

    // -------------------------------------------------------------------------
    // X1: email
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Front_page_is_the_email_form()
    {
        var html = await _browser.Html("/");

        Assert.Contains("action=\"/login\"", html);
        Assert.Contains("name=\"Email\"", html);
    }

    [Fact]
    public async Task Get_login_goes_to_the_front_page()
    {
        AssertRedirect(await _browser.Get("/login"), "/");
    }

    [Fact]
    public async Task A_registered_expert_gets_a_code_in_the_dev_mailbox()
    {
        var response = await _browser.RequestCode(Anna);

        AssertRedirect(response, "/login/code");
        Assert.Contains(Sent, await _browser.Html("/login/code"));
        Assert.Equal(1, Codes(Anna));
        Assert.Matches("^[0-9]{6}$", _app.LatestCode(Anna));
    }

    [Fact]
    public async Task A_standby_expert_gets_a_code()
    {
        await _browser.RequestCode(Bo);

        Assert.Equal(1, Messages(Bo));
    }

    [Fact]
    public async Task A_secretariat_user_gets_a_code()
    {
        await _browser.RequestCode(Sofie);

        Assert.Equal(1, Messages(Sofie));
    }

    [Fact]
    public async Task Capitals_and_spaces_in_the_email_make_no_difference()
    {
        await _browser.RequestCode("  ANNA.Demo@Example.COM ");

        Assert.Equal(1, Codes(Anna)); // stored in lower case
        Assert.Equal(1, Messages(Anna));
    }

    [Theory]
    [InlineData(Nobody)]
    [InlineData(Rita)] // Retired can't log in (SPEC decision 9)
    public async Task Unknown_and_retired_emails_look_exactly_the_same_but_get_nothing(string email)
    {
        var known = new Browser(_app);
        await known.UseEnglish();
        var knownResponse = await known.RequestCode(Anna);
        var knownPage = WithoutTokens(await known.Html("/login/code"));

        var response = await _browser.RequestCode(email);
        var page = WithoutTokens(await _browser.Html("/login/code"));

        Assert.Equal(knownResponse.StatusCode, response.StatusCode);
        Assert.Equal(knownResponse.Headers.Location, response.Headers.Location);
        Assert.Equal(knownPage, page);
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_login_codes WHERE email <> @Anna", new { Anna }));
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_dev_mailbox WHERE to_email <> @Anna", new { Anna }));
    }

    // A format error is wrong for everyone, registered or not, so showing it reveals nothing (decisions.md).
    [Theory]
    [InlineData("bo.demo@example.com)")]  // found in the E2 click-through
    [InlineData("bo.demo.example.com")]   // no @
    [InlineData("bo demo@example.com")]   // a space inside
    [InlineData("bo@@example.com")]
    [InlineData("bo.demo@")]
    [InlineData("@example.com")]
    [InlineData("bo.demo@example")]       // no dot in the domain
    [InlineData("bo.demo@example..com")]
    [InlineData("not an email at all")]
    [InlineData("søren@example.com")]     // ASCII only, as rules.py (decisions.md)
    [InlineData("søren@kø.example.com")]
    public async Task An_email_in_the_wrong_format_shows_an_error_and_sends_nothing(string email)
    {
        var response = await _browser.RequestCode(email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // stays on X1, no redirect to the code page
        var html = await Body(response);
        Assert.Contains(EmailInvalid, html);
        Assert.Contains($"value=\"{email}\"", html); // what was typed stays in the field
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_login_codes"));
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_dev_mailbox"));
    }

    [Fact]
    public async Task The_format_error_is_in_danish_too()
    {
        var browser = new Browser(_app); // Danish is the default

        var response = await browser.RequestCode("bo.demo@example.com)");

        Assert.Contains("Indtast en gyldig e-mailadresse.", await Body(response));
    }

    [Fact]
    public async Task A_registered_and_an_unknown_email_with_the_same_mistake_get_the_same_page()
    {
        var other = new Browser(_app);
        await other.UseEnglish();

        var registered = WithoutTokens(await Body(await _browser.RequestCode("anna.demo@example.com)")));
        var unknown = WithoutTokens(await Body(await other.RequestCode("nobody.xx@example.com)")));

        Assert.Contains(EmailInvalid, registered);
        Assert.Equal(
            registered.Replace("anna.demo@example.com)", "EMAIL", StringComparison.Ordinal),
            unknown.Replace("nobody.xx@example.com)", "EMAIL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_format_error_does_not_count_towards_the_lock()
    {
        for (var i = 0; i < 6; i++)
        {
            await _browser.RequestCode("anna.demo@example.com)");
        }

        AssertRedirect(await _browser.RequestCode(Anna), "/login/code");
        Assert.Equal(1, Codes(Anna));
    }

    /// <summary>The demo emails in rules.py, so the rule can never shut out a demo login.</summary>
    public static TheoryData<string> DemoEmailsInRulesPy()
    {
        var data = new TheoryData<string>();
        foreach (Match match in Regex.Matches(RepoFiles.Read("rules.py"), @"^DEMO_\w+ = ""([^""]+)""", RegexOptions.Multiline))
        {
            data.Add(match.Groups[1].Value);
        }

        return data;
    }

    [Fact]
    public void Rules_py_has_the_three_demo_emails() => Assert.Equal(3, DemoEmailsInRulesPy().Count);

    // Guard: valid emails still get through. Shapes seed.py makes (letters, digits, "-", "."),
    // capitals and spaces around the email.
    [Theory]
    [MemberData(nameof(DemoEmailsInRulesPy))]
    [InlineData(Anna)]
    [InlineData(Bo)]
    [InlineData(Sofie)]
    [InlineData(Nobody)]
    [InlineData("anne-marie.holm-jensen2@example.com")]
    [InlineData("  anna.demo@example.com  ")]
    [InlineData("ANNA.Demo@Example.COM")]
    [InlineData("first+tag@sub.example.com")]
    public async Task A_valid_email_goes_to_the_code_page(string email)
    {
        AssertRedirect(await _browser.RequestCode(email), "/login/code");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_email_shows_an_error_and_sends_nothing(string email)
    {
        var response = await _browser.RequestCode(email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(EmailRequired, await Body(response));
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_login_codes"));
    }

    [Fact]
    public async Task An_email_over_254_characters_shows_the_same_error()
    {
        var response = await _browser.RequestCode(new string('a', 243) + "@example.com"); // 255

        Assert.Contains(EmailRequired, await Body(response));
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_login_codes"));
    }

    [Fact]
    public async Task The_code_is_never_stored_in_plain_text_but_as_hmac_with_the_key()
    {
        await _browser.RequestCode(Anna);
        var code = _app.LatestCode(Anna);

        using var db = _app.Open();
        var hash = db.QuerySingle<string>("SELECT code_hash FROM ss_login_codes WHERE email = @Anna", new { Anna });

        Assert.DoesNotContain(code, hash);
        Assert.Equal(Hash("login", Anna, code), hash);
    }

    [Fact]
    public async Task A_code_expires_ten_minutes_after_it_was_made()
    {
        await _browser.RequestCode(Anna);
        using var db = _app.Open();
        var row = db.QuerySingle<(string Created, string Expires)>(
            "SELECT created_at, expires_at FROM ss_login_codes WHERE email = @Anna", new { Anna });

        Assert.Equal("2026-11-09T09:00:00Z", row.Created);
        Assert.Equal("2026-11-09T09:10:00Z", row.Expires);
    }

    // -------------------------------------------------------------------------
    // X2: code
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Code_page_without_an_email_in_progress_goes_to_the_front_page()
    {
        AssertRedirect(await _browser.Get("/login/code"), "/");
    }

    [Fact]
    public async Task Code_page_has_a_start_over_link()
    {
        await _browser.RequestCode(Anna);

        Assert.Matches("""<a href="/"[^>]*>Start over</a>""", await _browser.Html("/login/code"));
    }

    [Fact]
    public async Task The_right_code_logs_an_expert_in()
    {
        var response = await _browser.LogIn(Anna);

        AssertRedirect(response, "/profile");
        Assert.Equal(HttpStatusCode.OK, (await _browser.Get("/profile")).StatusCode);
    }

    [Fact]
    public async Task The_right_code_logs_a_secretariat_user_in()
    {
        var response = await _browser.LogIn(Sofie);

        AssertRedirect(response, "/staff");
        Assert.Equal(HttpStatusCode.OK, (await _browser.Get("/staff")).StatusCode);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("123 45")] // 5 digits once the space is removed
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("")]
    [InlineData("١٢٣٤٥٦")] // Arabic-Indic digits: .NET's \d would accept these
    public async Task A_code_that_is_not_6_digits_is_refused_without_using_a_try(string code)
    {
        await _browser.RequestCode(Anna);

        for (var i = 0; i < 5; i++)
        {
            var response = await _browser.SubmitCode(code);
            Assert.Contains(MustBeSixDigits, await Body(response));
        }

        AssertRedirect(await _browser.SubmitCode(_app.LatestCode(Anna)), "/profile");
    }

    // {0}..{5} are the six digits of the real code.
    [Theory]
    [InlineData("{0}{1}{2} {3}{4}{5}")] // "123 456", as people read it from a mail
    [InlineData(" {0} {1} {2} {3} {4} {5} ")]
    [InlineData("{0}{1}{2} {3}{4}{5}")] // non-breaking space, from pasted mail text
    [InlineData("{0}{1}{2}\t{3}{4}{5}")]
    public async Task Spaces_in_the_code_are_ignored(string layout)
    {
        await _browser.RequestCode(Anna);
        var digits = _app.LatestCode(Anna).Select(digit => (object)digit.ToString()).ToArray();

        var typed = string.Format(CultureInfo.InvariantCulture, layout, digits);

        AssertRedirect(await _browser.SubmitCode(typed), "/profile");
    }

    [Theory]
    [InlineData("en", "valid for 10 minutes")]
    [InlineData("da", "gælder i 10 minutter")]
    public async Task The_hint_and_the_mail_give_the_code_lifetime(string language, string expected)
    {
        var browser = new Browser(_app);
        await browser.Get($"/language/{language}?next=%2F");

        await browser.RequestCode(Anna);

        Assert.Contains(expected, await browser.Html("/login/code"));
        using var db = _app.Open();
        var body = db.QuerySingle<string>("SELECT body FROM ss_dev_mailbox WHERE to_email = @Anna", new { Anna });
        Assert.Contains(expected, body);
    }

    [Fact]
    public async Task A_wrong_code_is_refused()
    {
        await _browser.RequestCode(Anna);

        var response = await _browser.SubmitCode(WrongCodeFor(_app.LatestCode(Anna)));

        Assert.Contains(WrongOrExpired, await Body(response));
        AssertRedirect(await _browser.Get("/profile"), "/");
    }

    [Fact]
    public async Task A_code_works_just_before_ten_minutes()
    {
        await _browser.RequestCode(Anna);
        _app.Clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));

        AssertRedirect(await _browser.SubmitCode(_app.LatestCode(Anna)), "/profile");
    }

    [Fact]
    public async Task A_code_has_expired_at_ten_minutes()
    {
        await _browser.RequestCode(Anna);
        _app.Clock.Advance(TimeSpan.FromMinutes(10));

        var response = await _browser.SubmitCode(_app.LatestCode(Anna));

        Assert.Contains(WrongOrExpired, await Body(response));
    }

    [Fact]
    public async Task A_code_can_only_be_used_once()
    {
        await _browser.RequestCode(Anna);
        var copy = _browser.Clone(); // same email in progress, in a second browser
        var code = _app.LatestCode(Anna);

        AssertRedirect(await _browser.SubmitCode(code), "/profile");
        var second = await copy.SubmitCode(code);

        Assert.Contains(WrongOrExpired, await Body(second));
        AssertRedirect(await copy.Get("/profile"), "/");
    }

    [Fact]
    public async Task A_new_code_makes_the_older_one_invalid()
    {
        await _browser.RequestCode(Anna);
        var old = _app.LatestCode(Anna);
        await _browser.RequestCode(Anna);
        var current = _app.LatestCode(Anna);
        Assert.NotEqual(old, current); // one in a million chance of a false failure

        Assert.Contains(WrongOrExpired, await Body(await _browser.SubmitCode(old)));
        AssertRedirect(await _browser.SubmitCode(current), "/profile");
    }

    [Fact]
    public async Task A_code_for_an_email_change_cannot_log_in()
    {
        await _browser.RequestCode(Anna);
        using (var db = _app.Open())
        {
            db.Execute(
                """
                INSERT INTO ss_login_codes (email, purpose, code_hash, created_at, expires_at)
                VALUES (@Anna, 'email_change', @hash, '2026-11-09T09:00:00Z', '2026-11-09T09:10:00Z')
                """,
                new { Anna, hash = Hash("email_change", Anna, "123456") });
        }

        var response = await _browser.SubmitCode("123456");

        Assert.Contains(WrongOrExpired, await Body(response));
    }

    [Fact]
    public async Task A_newer_email_change_code_does_not_hide_the_login_code()
    {
        await _browser.RequestCode(Anna);
        var loginCode = _app.LatestCode(Anna);
        using (var db = _app.Open())
        {
            // Newer than the login code: only the newest code of the same purpose may count.
            db.Execute(
                """
                INSERT INTO ss_login_codes (email, purpose, code_hash, created_at, expires_at)
                VALUES (@Anna, 'email_change', @hash, '2026-11-09T09:00:00Z', '2026-11-09T09:10:00Z')
                """,
                new { Anna, hash = Hash("email_change", Anna, "654321") });
        }

        AssertRedirect(await _browser.SubmitCode(loginCode), "/profile");
    }

    // -------------------------------------------------------------------------
    // Five tries, then locked for 15 minutes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Four_wrong_codes_still_allow_the_right_one()
    {
        await _browser.RequestCode(Anna);
        await WrongCodes(4);

        AssertRedirect(await _browser.SubmitCode(_app.LatestCode(Anna)), "/profile");
    }

    [Fact]
    public async Task The_fifth_wrong_code_locks_the_email()
    {
        await _browser.RequestCode(Anna);
        await WrongCodes(4);

        var fifth = await _browser.SubmitCode(WrongCodeFor(_app.LatestCode(Anna)));

        Assert.Contains(LockedFifteen, await Body(fifth));
    }

    [Fact]
    public async Task While_locked_even_the_right_code_is_refused()
    {
        await _browser.RequestCode(Anna);
        var code = _app.LatestCode(Anna);
        await WrongCodes(5);

        var response = await _browser.SubmitCode(code);

        Assert.Contains("Too many attempts", await Body(response));
        AssertRedirect(await _browser.Get("/profile"), "/");
    }

    [Fact]
    public async Task While_locked_no_new_code_is_sent_and_the_minutes_left_are_shown()
    {
        await _browser.RequestCode(Anna);
        await WrongCodes(5);
        _app.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30));

        var response = await _browser.RequestCode(Anna);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Too many attempts. Try again in 10 minutes.", await Body(response)); // 9.5 rounded up
        Assert.Equal(1, Codes(Anna));
    }

    [Fact]
    public async Task One_minute_left_is_written_as_one_minute()
    {
        await _browser.RequestCode(Anna);
        await WrongCodes(5);
        _app.Clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(30));

        var response = await _browser.RequestCode(Anna);

        Assert.Contains("Too many attempts. Try again in 1 minute.", await Body(response));
    }

    [Fact]
    public async Task The_lock_opens_after_15_minutes_with_a_fresh_count()
    {
        await _browser.RequestCode(Anna);
        await WrongCodes(5);
        _app.Clock.Advance(TimeSpan.FromMinutes(15));

        AssertRedirect(await _browser.RequestCode(Anna), "/login/code");
        await WrongCodes(4); // a fresh count: 4 more wrong tries are allowed
        AssertRedirect(await _browser.SubmitCode(_app.LatestCode(Anna)), "/profile");
    }

    [Fact]
    public async Task The_right_code_resets_the_count()
    {
        await _browser.RequestCode(Anna);
        await WrongCodes(4);
        await _browser.SubmitCode(_app.LatestCode(Anna));
        await _browser.LogOut("/profile");

        await _browser.RequestCode(Anna);
        await WrongCodes(4);

        AssertRedirect(await _browser.SubmitCode(_app.LatestCode(Anna)), "/profile");
    }

    [Fact]
    public async Task Asking_for_a_new_code_does_not_reset_the_count()
    {
        await _browser.RequestCode(Anna);
        await WrongCodes(3);
        await _browser.RequestCode(Anna);
        await WrongCodes(1);

        var fifth = await _browser.SubmitCode(WrongCodeFor(_app.LatestCode(Anna)));

        Assert.Contains(LockedFifteen, await Body(fifth));
    }

    [Fact]
    public async Task One_email_in_different_capitals_shares_one_count()
    {
        await _browser.RequestCode("ANNA.DEMO@EXAMPLE.COM");
        await WrongCodes(3);
        await _browser.RequestCode(Anna);
        await WrongCodes(1);

        var fifth = await _browser.SubmitCode(WrongCodeFor(_app.LatestCode(Anna)));

        Assert.Contains(LockedFifteen, await Body(fifth));
    }

    [Fact]
    public async Task An_unknown_email_locks_the_same_way_so_the_lock_reveals_nothing()
    {
        await _browser.RequestCode(Nobody);
        for (var i = 0; i < 4; i++)
        {
            Assert.Contains(WrongOrExpired, await Body(await _browser.SubmitCode("123456")));
        }

        Assert.Contains(LockedFifteen, await Body(await _browser.SubmitCode("123456")));
        Assert.Contains("Too many attempts", await Body(await _browser.RequestCode(Nobody)));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>The hash the app must store: HMAC-SHA256 with CODE_KEY over "purpose:email:code", lower-case hex.</summary>
    private static string Hash(string purpose, string email, string code) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(CodeKey),
            Encoding.UTF8.GetBytes($"{purpose}:{email}:{code}")));

    /// <summary>A 6-digit code that is sure to differ from the right one.</summary>
    private static string WrongCodeFor(string? right) => right == "000000" ? "111111" : "000000";
}

internal static class AppFactoryExtensions
{
    /// <summary>The newest code in the mailbox, or null when nothing was sent (unknown email).</summary>
    public static string? LatestCodeOrNull(this AppFactory app)
    {
        using var db = app.Open();
        var body = db.QueryFirstOrDefault<string>("SELECT body FROM ss_dev_mailbox ORDER BY id DESC LIMIT 1");
        return body is null ? null : Regex.Match(body, @"\b[0-9]{6}\b").Value;
    }
}
