using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using static ExpertSelfService.Tests.AppFactory;

namespace ExpertSelfService.Tests;

/// <summary>
/// Only the host names of this machine are answered (CLAUDE.md ground rule 7, security review 24 Nov).
/// Listening on 127.0.0.1 keeps other machines out, but not a web page in the user's own browser: with DNS
/// rebinding, evil.example:5080 can point at 127.0.0.1, read the dev mailbox, log in and read the staff pages.
/// The browser still sends Host: evil.example, so refusing every other host name stops it.
/// </summary>
public sealed class HostTests : IAsyncLifetime, IDisposable
{
    private const string Evil = "evil.example:5080";

    private readonly AppFactory _app = new(); // Development, so the dev mailbox is on
    private readonly Browser _browser;

    public HostTests() => _browser = new Browser(_app);

    public Task InitializeAsync() => _browser.UseEnglish();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _app.Dispose();

    /// <summary>A GET with this Host header, sending the browser's cookies as usual.</summary>
    private static Task<HttpResponseMessage> GetAs(HttpClient http, string host, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Host = host;
        return http.SendAsync(request);
    }

    [Theory]
    [InlineData("127.0.0.1:5080")]
    [InlineData("localhost:5080")]
    [InlineData("LOCALHOST:5080")]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    public async Task This_machines_host_names_are_answered(string host)
    {
        var response = await GetAs(_browser.Http, host, "/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("evil.example:5080")]
    [InlineData("evil.example")]
    [InlineData("127.0.0.1.evil.example:5080")]
    [InlineData("localhost.evil.example:5080")]
    [InlineData("127.0.0.2:5080")]
    [InlineData("[::1]:5080")]
    public async Task Any_other_host_name_is_refused(string host)
    {
        var response = await GetAs(_browser.Http, host, "/");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_dev_mailbox_cant_be_read_under_another_host_name()
    {
        await _browser.RequestCode(Anna);

        var response = await GetAs(_browser.Http, Evil, "/dev/mailbox");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(_app.LatestCode(Anna), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task No_code_is_made_for_a_login_under_another_host_name()
    {
        // A real token and cookie, so only the host name can be the reason for the refusal.
        var token = await _browser.TokenFrom("/");
        var request = new HttpRequestMessage(HttpMethod.Post, "/login")
        {
            Content = new FormUrlEncodedContent(
            [
                KeyValuePair.Create("Email", Anna),
                KeyValuePair.Create("__RequestVerificationToken", token),
            ]),
        };
        request.Headers.Host = Evil;

        var response = await _browser.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_login_codes"));
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_dev_mailbox"));
    }

    [Fact]
    public async Task A_staff_page_cant_be_read_under_another_host_name()
    {
        Browser.AssertRedirect(await _browser.LogIn(Sofie), "/staff");

        var response = await GetAs(_browser.Http, Evil, "/staff");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("Anna", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_allowed_hosts_setting_cant_open_it_up_again()
    {
        // The list is set in code, so an AllowedHosts=* in the environment or a settings file changes nothing.
        using var loose = _app.WithWebHostBuilder(builder => builder.UseSetting("AllowedHosts", "*"));
        using var http = loose.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.BadRequest, (await GetAs(http, Evil, "/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAs(http, "127.0.0.1:5080", "/")).StatusCode);
    }
}
