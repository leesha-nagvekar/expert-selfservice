using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>
/// Story S3, screen S-C, step C: the back link above the expert's name returns to the list as it was left - S-A with
/// its search text, ticks and page, or S-B with its search text. The list's URL travels as ?back=; it comes from the
/// URL, so only /staff and /staff/pending (with or without a query) are kept, and anything else becomes /staff.
/// It is only ever a link, never a redirect target.
/// </summary>
public sealed class BackLinkTests : IAsyncLifetime, IDisposable
{
    private const string AnnaPage = "/staff/experts/1";

    // An S-A search with text and a tick, and the same as S-C's ?back= value.
    private const string Search = "/staff?q=Demo&status=1";
    private static readonly string SearchBack = Uri.EscapeDataString(Search);

    private readonly AppFactory _app = new();
    private readonly Browser _browser;

    public BackLinkTests() => _browser = new Browser(_app);

    public async Task InitializeAsync()
    {
        await _browser.UseEnglish();
        AssertRedirect(await _browser.LogIn(Sofie), "/staff");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _app.Dispose();

    // -------------------------------------------------------------------------
    // Helpers. The page's HTML contract:
    //   back link:   <p class="back"><a class="back-link" href="/staff?q=Demo&amp;status=1">
    //                <span aria-hidden="true">←</span> Back to search</a></p>, before <h1>
    //   decisions:   <input type="hidden" name="back" value="…"> in both forms, when back is not plain /staff
    // -------------------------------------------------------------------------

    /// <summary>The back link: its decoded href and its visible text. Fails if there is none.</summary>
    private static (string Href, string Text) BackLink(string html)
    {
        var match = Regex.Match(html, """<a class="back-link" href="([^"]*)"[^>]*>(.*?)</a>""", RegexOptions.Singleline);
        Assert.True(match.Success, "No back link");
        return (WebUtility.HtmlDecode(match.Groups[1].Value), Text(match.Groups[2].Value));
    }

    private static string Text(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();

    /// <summary>The ?back= value of a link, decoded, or null when the link has none.</summary>
    private static string? BackOf(string href)
    {
        var match = Regex.Match(WebUtility.HtmlDecode(href), @"[?&]back=([^&]*)");
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
    }

    /// <summary>The href of the name link to <paramref name="expertId"/>'s page.</summary>
    private static string NameLink(string html, long expertId)
    {
        var match = Regex.Match(html, $"""<a href="(/staff/experts/{expertId}(?:\?[^"]*)?)">""");
        Assert.True(match.Success, $"No name link to expert {expertId}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private void AddPendingChange()
    {
        using var db = _app.Open();
        db.Execute(
            """
            INSERT INTO ss_pending_changes (id, candidate_id, field, old_value, new_value, status, created_at) VALUES
                (1, 1, 'LastName', 'Demo', 'Holm', 'pending', '2026-11-08T10:00:00Z'),
                (2, 1, 'FirstName', 'Anna', 'Ana', 'pending', '2026-11-08T11:00:00Z');
            """);
    }

    private void AddLogEntries(int count)
    {
        using var db = _app.Open();
        for (var i = 1; i <= count; i++)
        {
            db.Execute(
                """
                INSERT INTO ss_change_log (candidate_id, actor_type, actor_id, field, old_value, new_value, created_at)
                VALUES (1, 'expert', 1, 'Street', 'old', @value, @createdAt)
                """,
                new { value = $"value {i}", createdAt = $"2026-11-01T{i:00}:00:00Z" });
        }
    }

    // -------------------------------------------------------------------------
    // The safety rule on its own
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("/staff")]
    [InlineData("/staff?")]
    [InlineData("/staff?q=Demo&status=1")]
    [InlineData("/staff?q=%C3%A6ble%20%26&field=EL&page=2")]
    [InlineData("/staff/pending")]
    [InlineData("/staff/pending?q=Anna")]
    public void A_list_url_is_kept(string back)
    {
        Assert.Equal(back, ExpertSelfService.Services.BackLink.Safe(back));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("staff")]
    [InlineData("https://evil.example")]
    [InlineData("https://evil.example/staff")]
    [InlineData("//evil.example")]
    [InlineData("//evil.example/staff")]
    [InlineData("/\\evil.example")]
    [InlineData("/staff?q=\\x")]
    [InlineData("/profile")]
    [InlineData("/staff/experts/2")]
    [InlineData("/staffx")]
    [InlineData("/staff/")]
    [InlineData("/staff#top")]
    [InlineData("/staff/../profile")]
    [InlineData("/staff/pending/../..")]
    [InlineData("/staff/pending/1/approve")]
    [InlineData("/staff?q=a\nb")]
    [InlineData("/staff\r\nSet-Cookie: x=1")]
    [InlineData("/staff?q=\u0007")]
    [InlineData("javascript:alert(1)")]
    [InlineData("%2F%2Fevil.example")]          // an encoded "//", arriving still encoded
    [InlineData("/%2Fevil.example")]
    [InlineData("/staff%2F..%2Fprofile")]
    [InlineData("%2Fstaff")]
    public void Anything_else_becomes_the_search(string? back)
    {
        Assert.Equal("/staff", ExpertSelfService.Services.BackLink.Safe(back));
    }

    [Fact]
    public void A_back_url_of_2000_characters_is_kept_and_one_more_is_not()
    {
        var longest = "/staff?q=" + new string('a', 2000 - "/staff?q=".Length);

        Assert.Equal(longest, ExpertSelfService.Services.BackLink.Safe(longest));
        Assert.Equal("/staff", ExpertSelfService.Services.BackLink.Safe(longest + "a"));
    }

    // -------------------------------------------------------------------------
    // The link on S-C
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Without_back_the_link_goes_to_the_search()
    {
        var html = await _browser.Html(AnnaPage);

        Assert.Equal(("/staff", "← Back to search"), BackLink(html));
        Assert.True(html.IndexOf("class=\"back-link\"", StringComparison.Ordinal) < html.IndexOf("<h1>", StringComparison.Ordinal), "The link comes before the name");
    }

    [Fact]
    public async Task A_search_back_is_kept_and_decoded_once()
    {
        var (href, text) = BackLink(await _browser.Html($"{AnnaPage}?back={SearchBack}"));

        Assert.Equal(Search, href);
        Assert.Equal("← Back to search", text);
    }

    [Fact]
    public async Task A_pending_back_says_back_to_pending_approvals()
    {
        var back = Uri.EscapeDataString("/staff/pending?q=Anna");

        Assert.Equal(("/staff/pending?q=Anna", "← Back to pending approvals"), BackLink(await _browser.Html($"{AnnaPage}?back={back}")));
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/profile")]
    [InlineData("/staff/../profile")]
    public async Task A_bad_back_falls_back_to_the_search_and_the_page_still_opens(string back)
    {
        var response = await _browser.Get($"{AnnaPage}?back={Uri.EscapeDataString(back)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        Assert.Equal(("/staff", "← Back to search"), BackLink(html));

        // No link or form points away. (The language switch's next= carries the whole URL, encoded, as before step C:
        // it only ever goes to a local URL, and back to this page, where back is checked again.)
        Assert.DoesNotMatch("""(href|action|value)="(https?:|//|/\\)""", html);
        Assert.DoesNotContain("name=\"back\"", html);
    }

    [Theory]
    [InlineData("%252F%252Fevil.example")]     // arrives as %2F%2Fevil.example
    [InlineData("%2F%2Fevil.example")]         // arrives as //evil.example
    [InlineData("%2F%255Cevil.example")]       // arrives as /%5Cevil.example
    public async Task An_encoded_double_slash_in_the_url_falls_back_to_the_search(string encodedBack)
    {
        var response = await _browser.Get($"{AnnaPage}?back={encodedBack}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        Assert.Equal(("/staff", "← Back to search"), BackLink(html));
        Assert.DoesNotMatch("""(href|action|value)="(https?:|//|/\\|%2F%2F)""", html);
    }

    [Fact]
    public async Task The_link_is_in_danish_too()
    {
        var danish = new Browser(_app);
        await danish.LogIn(Sofie);
        await danish.Get("/language/da?next=%2F");

        Assert.Equal("← Tilbage til søgningen", BackLink(await danish.Html(AnnaPage)).Text);
        var pending = Uri.EscapeDataString("/staff/pending");
        Assert.Equal("← Tilbage til Afventer godkendelse", BackLink(await danish.Html($"{AnnaPage}?back={pending}")).Text);
    }

    // -------------------------------------------------------------------------
    // The name links carry the list as it was
    // -------------------------------------------------------------------------

    [Fact]
    public async Task An_S_A_name_carries_the_search_with_its_ticks()
    {
        var html = await _browser.Html(Search);

        Assert.Equal(Search, BackOf(NameLink(html, 1)));
    }

    [Fact]
    public async Task An_S_A_name_on_page_2_carries_the_page()
    {
        // 30 more Active "Demo" experts, so the search has a second page (25 per page).
        using (var db = _app.Open())
        {
            for (var id = 100; id < 130; id++)
            {
                db.Execute(
                    """
                    INSERT INTO Candidates (Id, FirstName, LastName, Email, Status, RegistrationId, Created)
                    VALUES (@id, 'Zed', 'Demo', @email, 1, @number, '2020-01-01')
                    """,
                    new { id, email = $"zed{id}@example.com", number = $"9{id:00000}" });
            }
        }

        var html = await _browser.Html($"{Search}&page=2");
        var link = Regex.Match(html, """<a href="(/staff/experts/\d+\?[^"]*)">""");

        Assert.True(link.Success, "No name link on page 2");
        Assert.Equal($"{Search}&page=2", BackOf(link.Groups[1].Value));
    }

    [Fact]
    public async Task An_S_A_name_without_search_or_ticks_has_no_back()
    {
        // Plain /staff is where the link goes anyway, so the URL stays short.
        Assert.Equal(AnnaPage, NameLink(await _browser.Html("/staff"), 1));
    }

    [Fact]
    public async Task An_S_B_name_carries_the_search_text()
    {
        AddPendingChange();

        var html = await _browser.Html("/staff/pending?q=Anna");

        Assert.Equal("/staff/pending?q=Anna", BackOf(NameLink(html, 1)));
    }

    [Fact]
    public async Task An_S_B_name_without_search_text_carries_the_pending_list()
    {
        AddPendingChange();

        Assert.Equal("/staff/pending", BackOf(NameLink(await _browser.Html("/staff/pending"), 1)));
    }

    // -------------------------------------------------------------------------
    // back survives the log pager and the decisions
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_log_pager_keeps_back()
    {
        AddLogEntries(11);

        var html = await _browser.Html($"{AnnaPage}?back={SearchBack}");
        var next = Regex.Match(html, """<a href="([^"]*)" rel="next">""");

        Assert.True(next.Success, "No Next link");
        var href = WebUtility.HtmlDecode(next.Groups[1].Value);
        Assert.StartsWith($"{AnnaPage}?page=2", href);
        Assert.Equal(Search, BackOf(href));

        Assert.Equal(Search, BackLink(await _browser.Html(href)).Href);
    }

    [Fact]
    public async Task The_decision_forms_send_back_along()
    {
        AddPendingChange();

        var html = await _browser.Html($"{AnnaPage}?back={SearchBack}");

        Assert.Equal(4, Regex.Matches(html, """<input type="hidden" name="back" value="/staff\?q=Demo&amp;status=1">""").Count);
    }

    [Fact]
    public async Task Without_back_the_decision_forms_have_no_back_field()
    {
        AddPendingChange();

        Assert.DoesNotContain("name=\"back\"", await _browser.Html(AnnaPage));
    }

    [Fact]
    public async Task An_approval_returns_with_back()
    {
        AddPendingChange();

        var response = await _browser.Post($"{AnnaPage}/changes/1/approve", AnnaPage, ("back", Search));

        AssertRedirect(response, $"{AnnaPage}?back={SearchBack}");
        Assert.Equal(Search, BackLink(await _browser.Html($"{AnnaPage}?back={SearchBack}")).Href);
    }

    [Fact]
    public async Task A_rejection_returns_with_back()
    {
        AddPendingChange();

        var response = await _browser.Post($"{AnnaPage}/changes/1/reject", AnnaPage, ("Reason", "No"), ("back", Search));

        AssertRedirect(response, $"{AnnaPage}?back={SearchBack}");
    }

    [Fact]
    public async Task A_bad_back_in_a_decision_returns_without_it()
    {
        AddPendingChange();

        var response = await _browser.Post($"{AnnaPage}/changes/1/approve", AnnaPage, ("back", "https://evil.example"));

        AssertRedirect(response, AnnaPage);
    }

    /// <summary>
    /// The redirect after a decision always goes to the expert page itself; back only ever travels inside its query,
    /// whatever was sent - good, bad, another site or an encoded trick.
    /// </summary>
    [Theory]
    [InlineData("approve", "/staff?q=Demo&status=1")]
    [InlineData("approve", "/staff/pending?q=Anna")]
    [InlineData("approve", "https://evil.example")]
    [InlineData("approve", "//evil.example")]
    [InlineData("approve", "%2F%2Fevil.example")]
    [InlineData("approve", "/\\evil.example")]
    [InlineData("approve", "/profile")]
    [InlineData("reject", "/staff?q=Demo&status=1")]
    [InlineData("reject", "https://evil.example")]
    [InlineData("reject", "//evil.example")]
    [InlineData("reject", "%2F%2Fevil.example")]
    public async Task The_redirect_after_a_decision_never_goes_to_back(string action, string back)
    {
        AddPendingChange();

        var response = await _browser.Post($"{AnnaPage}/changes/1/{action}", AnnaPage, ("Reason", "No"), ("back", back));

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther, $"Expected a redirect, got {(int)response.StatusCode}");
        var location = response.Headers.Location!.OriginalString;
        Assert.False(response.Headers.Location.IsAbsoluteUri, $"The redirect must stay on this site: {location}");
        Assert.True(
            location == AnnaPage || location.StartsWith($"{AnnaPage}?back=", StringComparison.Ordinal),
            $"The redirect must go to the expert page: {location}");
    }

    [Fact]
    public async Task A_bad_reason_keeps_the_back_link_and_the_back_fields()
    {
        AddPendingChange();

        var response = await _browser.Post($"{AnnaPage}/changes/1/reject", AnnaPage, ("Reason", ""), ("back", Search));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        Assert.Contains("Write a reason for rejecting", Text(html));
        Assert.Equal(Search, BackLink(html).Href);
        Assert.Equal(4, Regex.Matches(html, """<input type="hidden" name="back" value="/staff\?q=Demo&amp;status=1">""").Count);
    }
}
