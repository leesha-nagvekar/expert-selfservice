using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertSelfService.Tests;

/// <summary>Lives only in the test project, so the real app has no page that crashes on purpose.</summary>
public class ThrowingController : Controller
{
    public const string Secret = "secret detail from the exception";

    [HttpGet("/test/boom")]
    [HttpPost("/test/boom")]
    public IActionResult Boom() => throw new InvalidOperationException(Secret);
}

public class ErrorPageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private HttpClient ClientWithThrowingPage(string environment) =>
        factory.WithWebHostBuilder(host =>
        {
            host.UseEnvironment(environment);
            host.ConfigureTestServices(services =>
                services.AddControllers().AddApplicationPart(typeof(ThrowingController).Assembly));
        }).CreateClient();

    private static void AssertOwnPage(string html, string title)
    {
        Assert.Contains(title, html);
        Assert.Contains("<header class=\"topbar\">", html);
    }

    [Theory]
    [InlineData("/nothing-here")]
    [InlineData("/missing.png")]
    [InlineData("/language/de")]
    [InlineData("/test/boom")] // the throwing page is not part of the real app
    public async Task Unknown_addresses_show_our_own_404(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertOwnPage(await response.Content.ReadAsStringAsync(), "Siden findes ikke");
    }

    [Fact]
    public async Task A_post_to_an_unknown_address_also_shows_our_own_404()
    {
        var response = await _client.PostAsync("/nothing-here", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertOwnPage(await response.Content.ReadAsStringAsync(), "Siden findes ikke");
    }

    // SPEC section 2: a refusal must look like "Page not found", so the page gives nothing away.
    [Fact]
    public async Task A_405_also_shows_page_not_found()
    {
        var response = await _client.PostAsync("/", null); // the front page only accepts GET

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        AssertOwnPage(await response.Content.ReadAsStringAsync(), "Siden findes ikke");
    }

    [Fact]
    public async Task The_404_follows_the_chosen_language()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/nothing-here");
        request.Headers.Add("Cookie", ".AspNetCore.Culture=c=en|uic=en");

        var response = await _client.SendAsync(request);

        AssertOwnPage(await response.Content.ReadAsStringAsync(), "Page not found");
    }

    [Fact]
    public async Task Switching_language_on_an_error_page_goes_to_the_front_page()
    {
        var html = await (await _client.GetAsync("/nothing-here")).Content.ReadAsStringAsync();

        Assert.Contains("href=\"/language/en?next=%2F\"", html);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task A_crash_shows_our_own_500_without_any_details(string environment)
    {
        var response = await ClientWithThrowingPage(environment).GetAsync("/test/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        AssertOwnPage(html, "Der skete en fejl");
        Assert.DoesNotContain(ThrowingController.Secret, html);
        Assert.DoesNotContain(nameof(InvalidOperationException), html);
        Assert.DoesNotContain(nameof(ThrowingController), html);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task A_crash_on_a_post_also_shows_our_own_500(string environment)
    {
        var response = await ClientWithThrowingPage(environment).PostAsync("/test/boom", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        AssertOwnPage(html, "Der skete en fejl");
        Assert.DoesNotContain(ThrowingController.Secret, html);
    }
}
