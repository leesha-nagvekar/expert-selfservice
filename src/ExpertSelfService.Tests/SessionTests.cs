using System.Net;
using Dapper;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>SPEC section 2: the session holds expert_id or staff_id, never both, and pages refuse the wrong kind.</summary>
public sealed class SessionTests : IAsyncLifetime, IDisposable
{
    private readonly AppFactory _app = new();
    private readonly Browser _browser;

    public SessionTests() => _browser = new Browser(_app);

    public Task InitializeAsync() => _browser.UseEnglish();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _app.Dispose();

    private static async Task AssertPageNotFound(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        var html = await Body(response);
        Assert.Contains("Page not found", html);
        Assert.Contains("<header class=\"topbar\">", html);
    }

    [Theory]
    [InlineData("/profile")]
    [InlineData("/staff")]
    public async Task Not_logged_in_goes_to_the_front_page_without_a_return_address(string url)
    {
        AssertRedirect(await _browser.Get(url), "/");
    }

    [Fact]
    public async Task An_expert_cannot_open_staff_pages()
    {
        await _browser.LogIn(Anna);

        await AssertPageNotFound(await _browser.Get("/staff"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_staff_user_cannot_open_expert_pages()
    {
        await _browser.LogIn(Sofie);

        await AssertPageNotFound(await _browser.Get("/profile"), HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(Anna, "/profile")]
    [InlineData(Sofie, "/staff")]
    public async Task Logged_in_the_front_page_goes_to_the_landing_page(string email, string landing)
    {
        await _browser.LogIn(email);

        AssertRedirect(await _browser.Get("/"), landing);
        AssertRedirect(await _browser.Get("/login/code"), landing);
    }

    [Fact]
    public async Task A_new_login_replaces_the_old_one_so_never_both()
    {
        await _browser.LogIn(Anna);

        // Log in as staff from the same browser, using the token on the expert's page.
        await _browser.RequestCode(Sofie, tokenPage: "/profile");
        var response = await _browser.SubmitCode(_app.LatestCode(Sofie), tokenPage: "/profile");

        AssertRedirect(response, "/staff");
        Assert.Equal(HttpStatusCode.OK, (await _browser.Get("/staff")).StatusCode);
        await AssertPageNotFound(await _browser.Get("/profile"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Log_out_is_shown_when_logged_in_and_ends_the_login()
    {
        Assert.DoesNotContain("action=\"/logout\"", await _browser.Html("/"));
        await _browser.LogIn(Anna);
        Assert.Contains("action=\"/logout\"", await _browser.Html("/profile"));

        var response = await _browser.LogOut("/profile");

        AssertRedirect(response, "/");
        AssertRedirect(await _browser.Get("/profile"), "/");
    }

    [Fact]
    public async Task Log_out_needs_a_post()
    {
        await _browser.LogIn(Anna);

        await AssertPageNotFound(await _browser.Get("/logout"), HttpStatusCode.MethodNotAllowed);
        Assert.Equal(HttpStatusCode.OK, (await _browser.Get("/profile")).StatusCode);
    }

    [Fact]
    public async Task Login_cookie_is_httponly_samesite_lax_and_ends_with_the_browser()
    {
        await _browser.RequestCode(Anna);
        var response = await _browser.SubmitCode(_app.LatestCode(Anna));

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            header => header.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_login_ends_after_60_idle_minutes()
    {
        await _browser.LogIn(Anna);
        _app.Clock.Advance(TimeSpan.FromMinutes(61));

        AssertRedirect(await _browser.Get("/profile"), "/");
    }

    [Fact]
    public async Task The_login_stays_while_in_use()
    {
        await _browser.LogIn(Anna);
        for (var i = 0; i < 4; i++)
        {
            _app.Clock.Advance(TimeSpan.FromMinutes(40));
            Assert.Equal(HttpStatusCode.OK, (await _browser.Get("/profile")).StatusCode);
        }
    }

    [Fact]
    public async Task A_post_without_the_antiforgery_token_is_refused_with_our_own_page()
    {
        var response = await _browser.Http.PostAsync(
            "/login", new FormUrlEncodedContent([KeyValuePair.Create("Email", Anna)]));

        await AssertPageNotFound(response, HttpStatusCode.BadRequest);
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_login_codes"));
    }

    [Fact]
    public async Task Switching_language_on_the_code_page_keeps_the_login_going()
    {
        await _browser.RequestCode(Anna);

        AssertRedirect(await _browser.Get("/language/da?next=%2Flogin%2Fcode"), "/login/code");
        var page = await _browser.Get("/login/code");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("""<html lang="da">""", await Body(page));

        AssertRedirect(await _browser.SubmitCode(_app.LatestCode(Anna)), "/profile");
    }

    // Retired or removed during a session: the login itself ends on the next request, on any page,
    // not just on pages that remember to check (decisions.md, "Logged out when removed or Retired").
    private void SetAnnasStatus(int status)
    {
        using var db = _app.Open();
        db.Execute("UPDATE Candidates SET Status = @status WHERE Id = 1", new { status });
    }

    private void RemoveAnna()
    {
        using var db = _app.Open();
        db.Execute(
            """
            DELETE FROM CandidateSkills WHERE CandidateId = 1;
            DELETE FROM CandidateTags WHERE CandidateId = 1;
            DELETE FROM ss_expert_company WHERE candidate_id = 1;
            DELETE FROM Candidates WHERE Id = 1;
            """);
    }

    private static void AssertLoginCookieDeleted(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies), "Expected the login cookie to be deleted");
        Assert.Contains(cookies, header =>
            header.StartsWith(".AspNetCore.Cookies=;", StringComparison.Ordinal)
            && header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
    }

    private async Task AssertFrontPageIsTheLoginForm()
    {
        var front = await _browser.Get("/");
        Assert.Equal(HttpStatusCode.OK, front.StatusCode);
        Assert.Contains("action=\"/login\"", await Body(front));
    }

    [Fact]
    public async Task Set_to_retired_the_login_ends_on_any_page_not_only_the_profile()
    {
        await _browser.LogIn(Anna);
        SetAnnasStatus(0);

        // The front page never reads the database itself. It must still see that the login has ended.
        var response = await _browser.Get("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertLoginCookieDeleted(response);
        await AssertFrontPageIsTheLoginForm();
    }

    [Fact]
    public async Task Removed_the_login_ends_on_any_page_not_only_the_profile()
    {
        await _browser.LogIn(Anna);
        RemoveAnna();

        var response = await _browser.Get("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertLoginCookieDeleted(response);
        await AssertFrontPageIsTheLoginForm();
    }

    /// <summary>Every GET page behind the Expert policy, read from the app's own route table, so E3–E7 pages are included.</summary>
    private List<string> ExpertPages() =>
        _app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == SessionClaims.ExpertPolicy))
            .Where(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Where(e => e.RoutePattern.Parameters.Count == 0)
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .ToList();

    [Fact]
    public async Task Set_to_retired_every_expert_page_sends_back_to_the_front_page()
    {
        var pages = ExpertPages();
        Assert.Contains("/profile", pages);

        foreach (var page in pages)
        {
            SetAnnasStatus(1);
            var browser = new Browser(_app);
            await browser.LogIn(Anna);
            SetAnnasStatus(0);

            AssertRedirect(await browser.Get(page), "/");
        }
    }

    [Theory]
    [InlineData(-1)] // Standby
    [InlineData(3)]  // Under observation
    [InlineData(7)]  // Unknown: SPEC decision 9, only Retired is shut out
    public async Task A_status_change_that_is_not_retired_keeps_the_login(int status)
    {
        await _browser.LogIn(Anna);
        SetAnnasStatus(status);

        Assert.Equal(HttpStatusCode.OK, (await _browser.Get("/profile")).StatusCode);
        AssertRedirect(await _browser.Get("/"), "/profile");
    }

    [Fact]
    public async Task A_staff_login_is_not_checked_against_the_experts()
    {
        await _browser.LogIn(Sofie);
        SetAnnasStatus(0); // Sofie's staff id is 1, the same number as Anna's expert id

        Assert.Equal(HttpStatusCode.OK, (await _browser.Get("/staff")).StatusCode);
    }

    // The expert's /profile shows their own data since E2 (ProfileTests). The staff page stays a placeholder until S1.
    [Fact]
    public async Task The_staff_landing_page_shows_no_personal_data()
    {
        await _browser.LogIn(Sofie);
        var html = await _browser.Html("/staff");

        Assert.DoesNotContain("Sofie", html);
        Assert.DoesNotContain(Sofie, html);
    }
}
