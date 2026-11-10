using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;

namespace ExpertSelfService.Tests;

/// <summary>
/// One browser: its own cookies, no automatic redirects, and forms posted with
/// the antiforgery token taken from a page, as a real browser would.
/// </summary>
public sealed class Browser
{
    private readonly AppFactory _app;

    public CookieContainer Cookies { get; }

    public HttpClient Http { get; }

    public Browser(AppFactory app, CookieContainer? cookies = null)
    {
        _app = app;
        Cookies = cookies ?? new CookieContainer();
        Http = app.CreateDefaultClient(new Uri("http://localhost"), new CookieContainerHandler(Cookies));
    }

    /// <summary>A second browser with a copy of this one's cookies, like a stolen or copied session.</summary>
    public Browser Clone()
    {
        var copy = new CookieContainer();
        foreach (Cookie cookie in Cookies.GetAllCookies())
        {
            copy.Add(new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain));
        }

        return new Browser(_app, copy);
    }

    public Task<HttpResponseMessage> Get(string url) => Http.GetAsync(url);

    public async Task<string> Html(string url) => await (await Get(url)).Content.ReadAsStringAsync();

    public Task UseEnglish() => Get("/language/en?next=%2F");

    public async Task<string> TokenFrom(string url)
    {
        var html = await Html(url);
        var match = Regex.Match(html, """<input[^>]*name="__RequestVerificationToken"[^>]*value="([^"]+)"[^>]*>""");
        Assert.True(match.Success, $"No antiforgery token on {url}");
        return match.Groups[1].Value;
    }

    public async Task<HttpResponseMessage> Post(string url, string tokenPage, params (string Name, string Value)[] fields)
    {
        var token = await TokenFrom(tokenPage);
        var form = fields
            .Select(field => KeyValuePair.Create(field.Name, field.Value))
            .Append(KeyValuePair.Create("__RequestVerificationToken", token));
        return await Http.PostAsync(url, new FormUrlEncodedContent(form));
    }

    public Task<HttpResponseMessage> RequestCode(string email, string tokenPage = "/") =>
        Post("/login", tokenPage, ("Email", email));

    public Task<HttpResponseMessage> SubmitCode(string code, string tokenPage = "/login/code") =>
        Post("/login/code", tokenPage, ("Code", code));

    public async Task<HttpResponseMessage> LogIn(string email)
    {
        await RequestCode(email);
        return await SubmitCode(_app.LatestCode(email));
    }

    public Task<HttpResponseMessage> LogOut(string tokenPage) => Post("/logout", tokenPage);

    public static async Task<string> Body(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    public static void AssertRedirect(HttpResponseMessage response, string location)
    {
        Assert.True(
            response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther,
            $"Expected a redirect to {location}, got {(int)response.StatusCode}");
        Assert.Equal(location, response.Headers.Location?.OriginalString);
    }
}
