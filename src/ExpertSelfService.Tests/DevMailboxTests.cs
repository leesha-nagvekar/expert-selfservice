using System.Net;
using static ExpertSelfService.Tests.AppFactory;

namespace ExpertSelfService.Tests;

/// <summary>Screen X3: the dev mailbox. Only in the Development environment.</summary>
public sealed class DevMailboxTests : IAsyncLifetime, IDisposable
{
    private readonly AppFactory _app = new(); // Development, clock at 09:00 UTC
    private readonly Browser _browser;

    public DevMailboxTests() => _browser = new Browser(_app);

    public Task InitializeAsync() => _browser.UseEnglish();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Lists_every_message_newest_first_with_the_code()
    {
        await _browser.RequestCode(Anna);
        _app.Clock.Advance(TimeSpan.FromMinutes(1));
        await _browser.RequestCode(Sofie);

        var response = await _browser.Get("/dev/mailbox");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(_app.LatestCode(Anna), html);
        Assert.Contains(_app.LatestCode(Sofie), html);
        Assert.True(html.IndexOf(Sofie, StringComparison.Ordinal) < html.IndexOf(Anna, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shows_the_time_in_copenhagen()
    {
        await _browser.RequestCode(Anna); // 09:00 UTC = 10:00 in Copenhagen in November

        var html = await _browser.Html("/dev/mailbox");

        Assert.Contains("10:00", html);
        Assert.DoesNotContain("09:00", html);
    }

    [Fact]
    public async Task English_times_are_british_day_month_and_24_hour()
    {
        await _browser.RequestCode(Anna); // 9 November 2026, 10:00 in Copenhagen

        var html = await _browser.Html("/dev/mailbox");

        Assert.Contains("09/11/2026 10:00", html);
        Assert.DoesNotMatch(@"\d:\d\d\s?(AM|PM)", html);
    }

    [Fact]
    public async Task Needs_no_login()
    {
        Assert.Equal(HttpStatusCode.OK, (await _browser.Get("/dev/mailbox")).StatusCode);
    }

    [Fact]
    public async Task The_code_page_links_to_the_mailbox_in_development()
    {
        await _browser.RequestCode(Anna);

        Assert.Contains("href=\"/dev/mailbox\"", await _browser.Html("/login/code"));
    }

    [Fact]
    public async Task Does_not_exist_outside_development()
    {
        using var production = AppFactory.With(environment: "Production");
        var browser = new Browser(production);
        await browser.UseEnglish();

        var response = await browser.Get("/dev/mailbox");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_code_page_has_no_mailbox_link_outside_development()
    {
        using var production = AppFactory.With(environment: "Production");
        var browser = new Browser(production);
        await browser.RequestCode(Anna);

        var html = await browser.Html("/login/code");

        Assert.Equal(HttpStatusCode.OK, (await browser.Get("/login/code")).StatusCode);
        Assert.DoesNotContain("/dev/mailbox", html);
    }
}
