using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ExpertSelfService.Tests;

public class LanguageTests(AppFactory factory) : IClassFixture<AppFactory>
{
    // A new client per test, so one test's cookie never reaches another test.
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string Next(string url) => Uri.EscapeDataString(url);

    [Fact]
    public async Task Switching_to_english_goes_back_and_shows_english()
    {
        var response = await _client.GetAsync($"/language/en?next={Next("/")}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);

        var html = await _client.GetStringAsync("/");
        Assert.Contains("""<html lang="en">""", html);
        Assert.Contains("Expert self-service", html);
    }

    [Fact]
    public async Task Switching_back_to_danish_works()
    {
        await _client.GetAsync($"/language/en?next={Next("/")}");
        await _client.GetAsync($"/language/da?next={Next("/")}");

        var html = await _client.GetStringAsync("/");
        Assert.Contains("""<html lang="da">""", html);
        Assert.Contains("Selvbetjening for skønsmænd", html);
    }

    [Fact]
    public async Task Language_cookie_is_httponly_and_samesite_lax()
    {
        var response = await _client.GetAsync($"/language/en?next={Next("/")}");

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Each_language_link_returns_to_the_page_it_was_clicked_on()
    {
        var danish = await _client.GetStringAsync("/");
        Assert.Contains($"href=\"/language/en?next={Next("/")}\"", danish);

        await _client.GetAsync($"/language/en?next={Next("/")}");
        var english = await _client.GetStringAsync("/");
        Assert.Contains($"href=\"/language/da?next={Next("/")}\"", english);
    }

    [Fact]
    public async Task A_page_address_on_this_site_is_kept_with_its_query()
    {
        var response = await _client.GetAsync($"/language/en?next={Next("/?a=1")}");

        Assert.Equal("/?a=1", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("//evil.example.com")]
    [InlineData("https://evil.example.com")]
    [InlineData("/\\evil.example.com")]
    [InlineData("evil")]
    [InlineData("")]
    public async Task Next_never_leaves_the_site(string next)
    {
        var response = await _client.GetAsync($"/language/en?next={Next(next)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task An_unknown_language_is_not_found_and_sets_no_cookie()
    {
        var response = await _client.GetAsync("/language/de");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task An_unknown_language_in_the_cookie_falls_back_to_danish()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", ".AspNetCore.Culture=c=de|uic=de");

        var response = await _client.SendAsync(request);

        Assert.Contains("""<html lang="da">""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_browser_language_is_ignored()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Accept-Language", "en");

        var response = await _client.SendAsync(request);

        Assert.Contains("""<html lang="da">""", await response.Content.ReadAsStringAsync());
    }
}
