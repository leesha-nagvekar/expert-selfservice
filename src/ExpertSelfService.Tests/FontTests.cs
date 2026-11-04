using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ExpertSelfService.Tests;

public class FontTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<List<string>> FontUrls()
    {
        var response = await _client.GetAsync("/fonts.css");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var css = await response.Content.ReadAsStringAsync();
        return Regex.Matches(css, """url\("([^"]+)"\)""").Select(match => match.Groups[1].Value).ToList();
    }

    [Fact]
    public async Task Font_urls_are_relative_so_nothing_is_loaded_from_another_site()
    {
        var urls = await FontUrls();

        Assert.Equal(6, urls.Count); // Sans 400, 500, 600, 700 and Mono 500, 600 (guide section 5)
        Assert.All(urls, url => Assert.StartsWith("fonts/", url));
    }

    [Fact]
    public async Task Fonts_load_as_woff2()
    {
        foreach (var url in await FontUrls())
        {
            var response = await _client.GetAsync("/" + url);

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("font/woff2", response.Content.Headers.ContentType?.MediaType);
        }
    }
}
