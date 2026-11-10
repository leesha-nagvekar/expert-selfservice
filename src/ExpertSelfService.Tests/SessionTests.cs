using System.Net;
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

    [Fact]
    public async Task Landing_pages_show_no_personal_data()
    {
        await _browser.LogIn(Anna);
        var html = await _browser.Html("/profile");

        Assert.DoesNotContain("Anna", html);
        Assert.DoesNotContain(Anna, html);
    }
}
