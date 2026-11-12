using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>
/// Story E4, screen X6: the expert changes their email. A code goes to the new address; the old email keeps
/// working until the code is confirmed. Then Candidates.Email, LastUpdated and the change log change together.
/// </summary>
public sealed class EmailChangeTests : IAsyncLifetime, IDisposable
{
    private const string Page = "/profile/email";
    private const string CodePage = "/profile/email/code";
    private const string NewEmail = "anna.new@example.com";
    private const string Today = "2026-11-09";
    private const string Now = "2026-11-09T09:00:00Z";

    private const string Required = "Enter your new email address.";
    private const string TooLong = "The email address can be at most 50 characters.";
    private const string Invalid = "Enter a valid email address.";
    private const string WrongDomain = "Use an address ending in @example.com.";
    private const string AlreadyYours = "This is already your email address.";
    private const string LockedFifteen = "Too many attempts. Try again in 15 minutes.";
    private const string WrongOrExpired = "The code is wrong or has expired";
    private const string CantBeUsed = "This email address can't be used. Choose another.";

    private readonly AppFactory _app = new();
    private readonly Browser _browser;

    public EmailChangeTests() => _browser = new Browser(_app);

    public async Task InitializeAsync()
    {
        await _browser.UseEnglish();
        AssertRedirect(await _browser.LogIn(Anna), "/profile");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _app.Dispose();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Task<HttpResponseMessage> Request(string newEmail, params (string Name, string Value)[] extra) =>
        Request(_browser, newEmail, extra);

    private static Task<HttpResponseMessage> Request(Browser browser, string newEmail, params (string Name, string Value)[] extra) =>
        browser.Post(Page, Page, [("NewEmail", newEmail), .. extra]);

    private Task<HttpResponseMessage> Confirm(string code, params (string Name, string Value)[] extra) =>
        _browser.Post(CodePage, CodePage, [("Code", code), .. extra]);

    private static string WrongCodeFor(string right) => right == "000000" ? "111111" : "000000";

    private string EmailOf(long id)
    {
        using var db = _app.Open();
        return db.QuerySingle<string>("SELECT Email FROM Candidates WHERE Id = @id", new { id });
    }

    private string? LastUpdatedOfAnna()
    {
        using var db = _app.Open();
        return db.QuerySingle<string?>("SELECT LastUpdated FROM Candidates WHERE Id = 1");
    }

    private sealed record ChangeRow(long CandidateId, string NewEmail, string? ConfirmedAt, string? CancelledAt);

    private List<ChangeRow> Changes()
    {
        using var db = _app.Open();
        return db.Query<ChangeRow>(
            """
            SELECT candidate_id AS CandidateId, new_email AS NewEmail, confirmed_at AS ConfirmedAt, cancelled_at AS CancelledAt
            FROM ss_email_changes ORDER BY id
            """).ToList();
    }

    private sealed record LogRow(string Field, string? OldValue, string? NewValue, string ActorType, long ActorId, long? PendingChangeId, string CreatedAt);

    private List<LogRow> ChangeLog()
    {
        using var db = _app.Open();
        return db.Query<LogRow>(
            """
            SELECT field AS Field, old_value AS OldValue, new_value AS NewValue, actor_type AS ActorType,
                   actor_id AS ActorId, pending_change_id AS PendingChangeId, created_at AS CreatedAt
            FROM ss_change_log ORDER BY id
            """).ToList();
    }

    private int Messages(string to) =>
        _app.Count("SELECT COUNT(*) FROM ss_dev_mailbox WHERE to_email = @to", new { to });

    private static string Decoded(string html) => WebUtility.HtmlDecode(html);

    private static string WithoutTokens(string html) =>
        Regex.Replace(html, """(<input[^>]*name="__RequestVerificationToken"[^>]*value=")[^"]*""", "$1");

    private static string Section(string html, string id)
    {
        var match = Regex.Match(html, $"""<section[^>]*\bid="{id}"[^>]*>(.*?)</section>""", RegexOptions.Singleline);
        Assert.True(match.Success, $"No section with id=\"{id}\"");
        return Decoded(match.Groups[1].Value);
    }

    private static void AssertFieldError(string html, string message)
    {
        var decoded = Decoded(html);
        Assert.Matches("""<input(?=[^>]*\bname="NewEmail")(?=[^>]*\baria-invalid="true")(?=[^>]*\baria-describedby="new-email-error")[^>]*>""", decoded);
        Assert.Matches($"""<p class="field-error" id="new-email-error">\s*{Regex.Escape(message)}\s*</p>""", decoded);
    }

    private void AssertNothingRequested()
    {
        Assert.Empty(Changes());
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_login_codes WHERE purpose = 'email_change'"));
        // Only change mails count: Anna's own address already has her login mail from InitializeAsync.
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_dev_mailbox WHERE subject = 'Confirm your new email'"));
        Assert.Equal(Anna, EmailOf(1));
    }

    // -------------------------------------------------------------------------
    // The form
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_profile_has_a_link_to_change_email()
    {
        var email = Section(await _browser.Html("/profile"), "email");

        Assert.Matches("""<a[^>]*href="/profile/email"[^>]*>\s*Change email\s*</a>""", email);
    }

    [Fact]
    public async Task The_form_shows_the_current_email()
    {
        var response = await _browser.Get(Page);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = Decoded(await Body(response));
        Assert.Contains("Change email", html);
        Assert.Contains(Anna, html);
        Assert.Matches("""<input(?=[^>]*\bname="NewEmail")[^>]*>""", html);
        Assert.Contains("Send code to new email", html);
    }

    // -------------------------------------------------------------------------
    // Asking for the change
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_new_email_gets_a_code_and_nothing_else_changes_yet()
    {
        var response = await Request("  Anna.New@Example.COM ");

        AssertRedirect(response, CodePage);
        Assert.Equal(new ChangeRow(1, NewEmail, null, null), Assert.Single(Changes()));
        Assert.Equal(1, Messages(NewEmail));
        Assert.Equal(1, Messages(Anna)); // only the login code from InitializeAsync
        Assert.Equal(1, _app.Count(
            "SELECT COUNT(*) FROM ss_dev_mailbox WHERE to_email = @NewEmail AND subject = 'Confirm your new email'", new { NewEmail }));
        Assert.Equal(1, _app.Count(
            "SELECT COUNT(*) FROM ss_login_codes WHERE email = @NewEmail AND purpose = 'email_change'", new { NewEmail }));
        Assert.Equal(Anna, EmailOf(1));
        Assert.Empty(ChangeLog());
    }

    [Fact]
    public async Task The_code_page_says_where_the_code_went()
    {
        await Request(NewEmail);

        var html = Decoded(await _browser.Html(CodePage));

        Assert.Contains($"We sent a code to {NewEmail}. Your current email keeps working until you confirm.", html);
        Assert.Contains("Confirm new email", html);
        Assert.Matches("""<a[^>]*href="/profile/email"[^>]*>""", html); // start over
    }

    [Fact]
    public async Task Until_confirmed_the_old_email_logs_in_and_the_new_one_does_not()
    {
        await Request(NewEmail);

        var old = new Browser(_app);
        AssertRedirect(await old.LogIn(Anna), "/profile");

        var other = new Browser(_app);
        await other.RequestCode(NewEmail);
        Assert.Equal(1, Messages(NewEmail)); // only the change code, no login code
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_login_codes WHERE email = @NewEmail AND purpose = 'login'", new { NewEmail }));
    }

    [Fact]
    public async Task The_code_page_without_a_request_goes_back_to_the_form()
    {
        AssertRedirect(await _browser.Get(CodePage), Page);
    }

    // -------------------------------------------------------------------------
    // Checks on the new email: the error next to the field, nothing sent
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("", Required)]
    [InlineData("   ", Required)]
    [InlineData("anna.new@example", Invalid)]
    [InlineData("anna new@example.com", Invalid)]
    [InlineData("søren@example.com", Invalid)]
    [InlineData("anna.new@gmail.com", WrongDomain)]
    [InlineData("anna.new@example.com.evil.dk", WrongDomain)]
    [InlineData("ANNA.DEMO@example.com", AlreadyYours)]
    public async Task A_wrong_new_email_shows_the_error_and_sends_nothing(string newEmail, string message)
    {
        var response = await Request(newEmail);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        AssertFieldError(html, message);
        if (!string.IsNullOrWhiteSpace(newEmail))
        {
            Assert.Matches($"""<input(?=[^>]*\bname="NewEmail")(?=[^>]*\bvalue="{Regex.Escape(newEmail)}")[^>]*>""", Decoded(html));
        }

        AssertNothingRequested();
    }

    [Fact]
    public async Task A_locked_new_email_shows_the_lock_and_sends_nothing()
    {
        using (var db = _app.Open())
        {
            db.Execute(
                "INSERT INTO ss_login_lockouts (email, failed_count, locked_until) VALUES (@NewEmail, 5, '2026-11-09T09:15:00Z')",
                new { NewEmail });
        }

        var html = await Body(await Request(NewEmail));

        AssertFieldError(html, LockedFifteen);
        AssertNothingRequested();
    }

    [Theory]
    [InlineData("", "Skriv din nye e-mailadresse.")]
    [InlineData("anna.new@example", "Indtast en gyldig e-mailadresse.")]
    [InlineData("anna.new@gmail.com", "Brug en adresse, der ender på @example.com.")]
    [InlineData("anna.demo@example.com", "Det er allerede din e-mailadresse.")]
    public async Task The_errors_are_in_danish_too(string newEmail, string message)
    {
        var danish = new Browser(_app);
        await danish.LogIn(Anna);

        AssertFieldError(await Body(await Request(danish, newEmail)), message);
    }

    private static int EmailMaxInRulesPy()
    {
        var match = Regex.Match(RepoFiles.Read("rules.py"), "\"Email\": (\\d+),");
        Assert.True(match.Success, "No MAX_LENGTHS entry for Email in rules.py");
        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string DomainInRulesPy()
    {
        var match = Regex.Match(RepoFiles.Read("rules.py"), "EMAIL_DOMAIN = \"([^\"]+)\"");
        Assert.True(match.Success, "No EMAIL_DOMAIN in rules.py");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task The_new_email_may_be_as_long_as_rules_py_allows_and_no_longer()
    {
        var max = EmailMaxInRulesPy();
        var domain = DomainInRulesPy();
        Assert.Equal("@example.com", domain);
        string Email(int length) => new string('a', length - domain.Length) + domain;

        var tooLong = await Request(Email(max + 1));
        AssertFieldError(await Body(tooLong), TooLong);
        AssertNothingRequested();

        AssertRedirect(await Request(Email(max)), CodePage);
    }

    // -------------------------------------------------------------------------
    // Not revealed: an address in use looks exactly like a free one until the code is typed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task An_address_in_use_gets_the_same_answer_as_a_free_one()
    {
        var used = await Request(Bo);
        var usedPage = WithoutTokens(Decoded(await _browser.Html(CodePage))).Replace(Bo, "EMAIL", StringComparison.Ordinal);

        var free = await Request(NewEmail);
        var freePage = WithoutTokens(Decoded(await _browser.Html(CodePage))).Replace(NewEmail, "EMAIL", StringComparison.Ordinal);

        Assert.Equal(free.StatusCode, used.StatusCode);
        Assert.Equal(free.Headers.Location, used.Headers.Location);
        Assert.Equal(freePage, usedPage);
        Assert.Equal(1, Messages(Bo)); // the change code went to Bo's address, as it would to any address
    }

    // -------------------------------------------------------------------------
    // Confirming
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_right_code_changes_the_email_last_updated_and_the_change_log_together()
    {
        await Request(NewEmail);

        var response = await Confirm(_app.LatestCode(NewEmail));

        AssertRedirect(response, "/profile");
        Assert.Equal(NewEmail, EmailOf(1));
        Assert.Equal(Today, LastUpdatedOfAnna());
        Assert.Equal(new LogRow("Email", Anna, NewEmail, "expert", 1, null, Now), Assert.Single(ChangeLog()));
        var change = Assert.Single(Changes());
        Assert.NotNull(change.ConfirmedAt);
        Assert.Null(change.CancelledAt);
    }

    [Fact]
    public async Task After_confirming_the_profile_shows_the_new_email_and_saved_once()
    {
        await Request(NewEmail);
        await Confirm(_app.LatestCode(NewEmail));

        var email = Regex.Replace(Section(await _browser.Html("/profile"), "email"), @"\s+", " ");
        Assert.Contains(NewEmail, email);
        Assert.Matches("""<svg[^>]*aria-hidden="true"[^>]*>.*?</svg> Saved""", email);

        Assert.DoesNotContain("Saved", Section(await _browser.Html("/profile"), "email"));
    }

    [Fact]
    public async Task After_confirming_the_new_email_logs_in_and_the_old_one_gets_no_code()
    {
        await Request(NewEmail);
        await Confirm(_app.LatestCode(NewEmail));
        var loginCodesForAnna = _app.Count("SELECT COUNT(*) FROM ss_login_codes WHERE email = @Anna", new { Anna });

        AssertRedirect(await new Browser(_app).LogIn(NewEmail), "/profile");

        await new Browser(_app).RequestCode(Anna);
        Assert.Equal(loginCodesForAnna, _app.Count("SELECT COUNT(*) FROM ss_login_codes WHERE email = @Anna", new { Anna }));
    }

    [Fact]
    public async Task The_code_may_be_typed_with_a_space()
    {
        await Request(NewEmail);
        var code = _app.LatestCode(NewEmail);

        AssertRedirect(await Confirm($"{code[..3]} {code[3..]}"), "/profile");
        Assert.Equal(NewEmail, EmailOf(1));
    }

    [Fact]
    public async Task A_wrong_code_shows_the_error_and_changes_nothing()
    {
        await Request(NewEmail);

        var response = await Confirm(WrongCodeFor(_app.LatestCode(NewEmail)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(WrongOrExpired, await Body(response));
        Assert.Equal(Anna, EmailOf(1));
        Assert.Empty(ChangeLog());
        Assert.Equal(1, _app.Count("SELECT failed_count FROM ss_login_lockouts WHERE email = @NewEmail", new { NewEmail }));
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_new_email_but_not_the_experts_own_login()
    {
        await Request(NewEmail);
        var wrong = WrongCodeFor(_app.LatestCode(NewEmail));
        for (var i = 0; i < 4; i++)
        {
            await Confirm(wrong);
        }

        var fifth = await Confirm(wrong);

        Assert.Contains(LockedFifteen, await Body(fifth));
        Assert.Equal(1, _app.Count("SELECT COUNT(*) FROM ss_login_lockouts WHERE email = @NewEmail AND locked_until IS NOT NULL", new { NewEmail }));
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_login_lockouts WHERE email = @Anna", new { Anna }));
        AssertRedirect(await new Browser(_app).LogIn(Anna), "/profile");

        // Locked: even the right code is refused.
        await Confirm(_app.LatestCode(NewEmail));
        Assert.Equal(Anna, EmailOf(1));
    }

    [Fact]
    public async Task The_right_code_clears_the_count()
    {
        await Request(NewEmail);
        var code = _app.LatestCode(NewEmail);
        await Confirm(WrongCodeFor(code));
        await Confirm(WrongCodeFor(code));
        // First prove the two wrong tries were counted on the new email, so "cleared" below means something.
        Assert.Equal(2, _app.Count("SELECT failed_count FROM ss_login_lockouts WHERE email = @NewEmail", new { NewEmail }));

        AssertRedirect(await Confirm(code), "/profile");

        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_login_lockouts WHERE email = @NewEmail", new { NewEmail }));
    }

    [Fact]
    public async Task A_code_older_than_ten_minutes_is_refused()
    {
        await Request(NewEmail);
        var code = _app.LatestCode(NewEmail);
        _app.Clock.Advance(TimeSpan.FromMinutes(11));

        Assert.Contains(WrongOrExpired, await Body(await Confirm(code)));
        Assert.Equal(Anna, EmailOf(1));
    }

    [Fact]
    public async Task A_code_works_only_once()
    {
        await Request(NewEmail);
        var code = _app.LatestCode(NewEmail);
        await Confirm(code);

        // No open request any more: back to the form.
        AssertRedirect(await _browser.Get(CodePage), Page);
    }

    [Fact]
    public async Task A_new_request_cancels_the_older_one()
    {
        await Request("anna.first@example.com");
        var first = _app.LatestCode("anna.first@example.com");
        await Request(NewEmail);
        var second = _app.LatestCode(NewEmail);

        Assert.Contains(WrongOrExpired, await Body(await Confirm(first)));
        AssertRedirect(await Confirm(second), "/profile");

        Assert.Equal(NewEmail, EmailOf(1));
        var changes = Changes();
        Assert.Equal(2, changes.Count);
        Assert.NotNull(changes[0].CancelledAt);
        Assert.Null(changes[0].ConfirmedAt);
        Assert.NotNull(changes[1].ConfirmedAt);
    }

    // -------------------------------------------------------------------------
    // In use: only said after the right code, and checked again when confirming
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(Bo)]
    [InlineData(Rita)]   // Retired, but still a Candidates row
    [InlineData(Sofie)]  // a staff user
    public async Task An_address_in_use_cant_be_used_after_the_code(string used)
    {
        await Request(used);

        var response = await Confirm(_app.LatestCode(used));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertFieldError(await Body(response), CantBeUsed);
        Assert.Equal(Anna, EmailOf(1));
        Assert.Equal(Bo, EmailOf(2));
        Assert.Empty(ChangeLog());
        Assert.NotNull(Assert.Single(Changes()).CancelledAt);
    }

    [Fact]
    public async Task An_address_taken_between_request_and_confirm_cant_be_used()
    {
        await Request(NewEmail);
        var code = _app.LatestCode(NewEmail);
        using (var db = _app.Open())
        {
            db.Execute("UPDATE Candidates SET Email = @NewEmail WHERE Id = 2", new { NewEmail });
        }

        AssertFieldError(await Body(await Confirm(code)), CantBeUsed);
        Assert.Equal(Anna, EmailOf(1));
        Assert.Empty(ChangeLog());
    }

    // -------------------------------------------------------------------------
    // Purposes don't mix
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_change_code_cannot_log_anyone_in()
    {
        await Request(NewEmail);
        var changeCode = _app.LatestCode(NewEmail);

        var other = new Browser(_app);
        await other.UseEnglish();
        await other.RequestCode(NewEmail);
        var response = await other.SubmitCode(changeCode);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(WrongOrExpired, await Body(response));
        AssertRedirect(await other.Get("/profile"), "/");
    }

    // -------------------------------------------------------------------------
    // Security
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Id")]
    [InlineData("expertId")]
    [InlineData("CandidateId")]
    public async Task An_id_in_the_forms_is_ignored(string name)
    {
        await Request(NewEmail, (name, "2"));
        Assert.Equal(1, Assert.Single(Changes()).CandidateId);

        AssertRedirect(await Confirm(_app.LatestCode(NewEmail), (name, "2")), "/profile");

        Assert.Equal(NewEmail, EmailOf(1));
        Assert.Equal(Bo, EmailOf(2));
        Assert.All(ChangeLog(), row => Assert.Equal(1, row.ActorId));
    }

    [Fact]
    public async Task Staff_cannot_use_any_of_the_pages()
    {
        var staff = new Browser(_app);
        await staff.LogIn(Sofie);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Get(Page)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Get(CodePage)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Post(Page, "/staff", ("NewEmail", NewEmail))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Post(CodePage, "/staff", ("Code", "123456"))).StatusCode);
        Assert.Empty(Changes());
    }

    [Theory]
    [InlineData(Page)]
    [InlineData(CodePage)]
    public async Task Not_logged_in_goes_to_the_front_page(string url)
    {
        AssertRedirect(await new Browser(_app).Get(url), "/");
    }

    [Fact]
    public async Task A_post_without_the_antiforgery_token_is_refused()
    {
        var response = await _browser.Http.PostAsync(
            Page, new FormUrlEncodedContent([KeyValuePair.Create("NewEmail", NewEmail)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Changes());
    }

    [Fact]
    public async Task An_id_in_the_path_gives_page_not_found()
    {
        var response = await _browser.Get("/profile/email/2");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await Body(response));
    }
}
