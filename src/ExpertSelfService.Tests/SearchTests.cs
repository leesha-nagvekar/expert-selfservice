using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>
/// Story S1, screen S-A: the secretariat searches experts. One search box ("Name, email or postcode", every word
/// must match) and tick-box groups in a sidebar: Region, Role, Field of expertise, Status and "Only experts with
/// pending changes". OR inside a group, AND between groups, no tick = all. Retired experts are shown.
/// Everything is in the query string of GET /staff (SPEC S-A).
/// </summary>
public sealed partial class SearchTests : IAsyncLifetime, IDisposable
{
    private const string Page = "/staff";

    // VBA numbers of the five experts after SetUpExperts.
    private const string AnnaDemo = "900001";      // Active, 2100 København Ø (Hovedstaden), EL/Maskinanlæg/Varmepumpe, Skønsmand
    private const string BoDemo = "900002";        // Standby, 5000 Odense C (Syddanmark), Fiber, Sagkyndig
    private const string RitaRetired = "900003";   // Retired, no postcode, no fields, no roles
    private const string Soeren = "900004";        // Søren Kragh Østergaard, Accepted, 8000 Aarhus C (Midtjylland), EL/Fiber, Sagkyndig/Mediator
    private const string AnnaJensen = "900005";    // Active, 8000 Aarhus C (Midtjylland), Tagdækning, no roles

    // Sorted by last name, then first name, in Danish order: Demo Anna, Demo Bo, Jensen, Retired, Østergaard.
    private static readonly string[] Everyone = [AnnaDemo, BoDemo, AnnaJensen, RitaRetired, Soeren];

    private readonly AppFactory _app = new();
    private readonly Browser _browser;

    public SearchTests() => _browser = new Browser(_app);

    public async Task InitializeAsync()
    {
        SetUpExperts();
        await _browser.UseEnglish();
        AssertRedirect(await _browser.LogIn(Sofie), Page);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _app.Dispose();

    // -------------------------------------------------------------------------
    // Test data: added here, not in AppFactory, so no other test class changes.
    // -------------------------------------------------------------------------

    private void SetUpExperts()
    {
        using var db = _app.Open();
        db.Execute(
            """
            -- VBA's regions with their real names and DisplayOrder (data/lookups/Regions.csv).
            UPDATE Regions SET RegionName = 'Norge', DisplayOrder = 7 WHERE Id = 2;
            UPDATE Regions SET RegionName = 'Midtjylland', DisplayOrder = 4 WHERE Id = 4;
            UPDATE Regions SET RegionName = 'Syddanmark', DisplayOrder = 5 WHERE Id = 6;
            INSERT INTO Regions (Id, RegionName, DisplayOrder) VALUES
                (1, 'Sverige', 6), (3, 'Andre', 8), (5, 'Nordjylland', 3), (7, 'Hovedstaden', 1), (8, 'Sjælland', 2);
            UPDATE Cities SET RegionId = 7 WHERE Id = 2001; -- København Ø

            -- All 12 roles (data/lookups/ProfessionTags.csv): more than 8, so the group gets "Show all".
            INSERT INTO ProfessionTags (Id, Tag) VALUES
                (34, 'Faglig dommer'), (37, 'Mediator'), (38, 'Enedommer'), (39, 'Mægler'), (40, 'Opmand'),
                (41, 'Mentor'), (42, 'Staderegistrant'), (43, 'Voldgiftsdommer'), (44, 'Bisidder'), (45, 'Tilsynsførende');

            -- Two more fields, so Fags has 10: more than 8.
            INSERT INTO Fags (Id, Fag) VALUES (288, 'Tagdækning'), (289, 'Beton');

            INSERT INTO Candidates
                (Id, FirstName, MiddleName, LastName, Email, Status, RegistrationId, Created, Street, ZipcodeId, Notes) VALUES
                (4, 'Søren', 'Kragh', 'Østergaard', 'soeren.oestergaard@example.com', 2, '900004', '2018-05-01',
                 'Banegårdspladsen 1', 2003, 'NOTE-SECRET-4'),
                (5, 'Anna', NULL, 'Jensen', 'anna.jensen@example.com', 1, '900005', '2021-09-01',
                 'Søndergade 2', 2003, NULL);
            UPDATE Candidates SET Notes = 'NOTE-SECRET-1' WHERE Id = 1;
            INSERT INTO CandidateSkills (CandidateId, Skill) VALUES (4, 'EL'), (4, 'Fiber'), (5, 'Tagdækning');
            INSERT INTO CandidateTags (CandidateId, TagId) VALUES (4, 36), (4, 37);

            -- Anna Demo: two pending. Bo: one approved and one rejected, so none pending.
            INSERT INTO ss_pending_changes (candidate_id, field, old_value, new_value, status, reason, created_at, decided_by, decided_at) VALUES
                (1, 'FirstName', 'Anna', 'Anne', 'pending', NULL, '2026-11-08T10:00:00Z', NULL, NULL),
                (1, 'Skills', 'EL; Maskinanlæg; Varmepumpe', 'EL; Varmepumpe', 'pending', NULL, '2026-11-08T11:00:00Z', NULL, NULL),
                (2, 'LastName', 'Demo', 'Dammo', 'approved', NULL, '2026-11-01T10:00:00Z', 1, '2026-11-02T10:00:00Z'),
                (2, 'FirstName', 'Bo', 'Bob', 'rejected', 'Not a name change', '2026-11-03T10:00:00Z', 1, '2026-11-04T10:00:00Z');
            """);
    }

    // -------------------------------------------------------------------------
    // Helpers. The page's HTML contract:
    //   result row:   <tr class="result"> … <td class="vba-no …">900001</td> … <td class="pending-count">2</td>
    //   filter group: <details class="filter-group" data-group="role" open="open"> … </details>
    //                 (the pending box: <fieldset class="filter-group" data-group="pending"> … </fieldset>)
    //   badge:        <span class="badge"> … 2 ticked … </span> in the group's summary
    //   tick box:     <input type="checkbox" name="role" value="36" …>
    //   more than 8:  every value inside <div class="filter-scroll">
    //   find box:     <input type="search" class="filter-find" aria-label="Find a role" …>
    //   page links:   <div class="pager"> … <a href="/staff?…&page=2"> … </div>
    // -------------------------------------------------------------------------

    private Task<string> Search(string query) => _browser.Html($"{Page}?{query}");

    private static List<string> Rows(string html) =>
        [.. RowPattern().Matches(html).Select(match => match.Value)];

    private static List<string> Results(string html) =>
        [.. Rows(html).Select(row => VbaPattern().Match(row).Groups[1].Value)];

    private static string Row(string html, string vbaNumber) =>
        Rows(html).Single(row => VbaPattern().Match(row).Groups[1].Value == vbaNumber);

    private static string Group(string html, string name)
    {
        var match = Regex.Match(
            html, $"""<(details|fieldset) class="filter-group" data-group="{name}"[^>]*>.*?</\1>""", RegexOptions.Singleline);
        Assert.True(match.Success, $"No filter group '{name}'");
        return match.Value;
    }

    /// <summary>The group's opening tag, which says whether it starts open.</summary>
    private static string GroupTag(string html, string name) =>
        Regex.Match(Group(html, name), "^<[^>]*>").Value;

    private static string Pager(string html)
    {
        var match = Regex.Match(html, """<div class="pager">.*?</nav>""", RegexOptions.Singleline);
        return match.Success ? match.Value : "";
    }

    /// <summary>The page links' href values, HTML-decoded.</summary>
    private static List<string> Links(string pagerHtml) =>
        [.. Regex.Matches(pagerHtml, """<a href="([^"]*)"[^>]*>""")
            .Select(match => System.Net.WebUtility.HtmlDecode(match.Groups[1].Value))];

    /// <summary>30 more experts, Paging01 … Paging30, so there are 35 and two pages.</summary>
    private void AddPagingExperts()
    {
        using var db = _app.Open();
        var experts = Enumerable.Range(1, 30).Select(n => new
        {
            Id = 100 + n,
            LastName = $"Paging{n:00}",
            Email = $"paging{n:00}@example.com",
            RegistrationId = $"9101{n:00}",
        });
        db.Execute(
            """
            INSERT INTO Candidates (Id, FirstName, LastName, Email, Status, RegistrationId, Created)
            VALUES (@Id, 'Test', @LastName, @Email, 1, @RegistrationId, '2020-01-01')
            """,
            experts);
    }

    // Sorted: Demo Anna, Demo Bo, Jensen, Paging01 … Paging30, Retired, Østergaard.
    private static readonly string[] EveryoneWithPaging =
        [AnnaDemo, BoDemo, AnnaJensen, .. Enumerable.Range(1, 30).Select(n => $"9101{n:00}"), RitaRetired, Soeren];

    private static List<string> Values(string groupHtml, string name) =>
        [.. Regex.Matches(groupHtml, $"""<input[^>]*type="checkbox"[^>]*name="{name}"[^>]*value="([^"]*)["]""")
            .Select(match => match.Groups[1].Value)];

    private static List<string> Ticked(string groupHtml, string name) =>
        [.. Regex.Matches(groupHtml, $"""<input[^>]*type="checkbox"[^>]*name="{name}"[^>]*value="([^"]*)"[^>]*checked""")
            .Select(match => match.Groups[1].Value)];

    /// <summary>The tick boxes inside the group's scroll box, or null when it has none.</summary>
    private static List<string>? Scrolled(string groupHtml, string name)
    {
        var at = groupHtml.IndexOf("<div class=\"filter-scroll\">", StringComparison.Ordinal);
        return at < 0 ? null : Values(groupHtml[at..], name);
    }

    [GeneratedRegex("""<tr class="result".*?</tr>""", RegexOptions.Singleline)]
    private static partial Regex RowPattern();

    [GeneratedRegex("""class="vba-no[^"]*">\s*([0-9]+)\s*<""")]
    private static partial Regex VbaPattern();

    [GeneratedRegex("""class="pending-count">\s*([0-9]+)\s*<""")]
    private static partial Regex PendingPattern();

    // -------------------------------------------------------------------------
    // The page
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_page_has_the_search_box_the_sidebar_and_the_script()
    {
        var html = await _browser.Html(Page);

        Assert.Contains("<h1>Search experts</h1>", html);
        Assert.Matches("""<form[^>]*method="get"[^>]*action="/staff["]""", html);
        Assert.Matches("""<label[^>]*for="q"[^>]*>Name, email or postcode</label>""", html);
        Assert.Matches("""<input[^>]*name="q"[^>]*maxlength="254["]""", html);
        Assert.Contains(">Search</button>", html);
        Assert.Contains("""<a href="/staff">Clear filters</a>""", html);
        Assert.Contains("""<script src="/staff-search.js""", html);
        foreach (var group in new[] { "region", "role", "field", "status", "pending" })
        {
            Group(html, group);
        }

        Assert.Contains("Only experts with pending changes", Group(html, "pending"));
    }

    [Fact]
    public async Task Clear_filters_sits_at_the_top_of_the_sidebar()
    {
        var html = await _browser.Html(Page);

        var clear = html.IndexOf("""<a href="/staff">Clear filters</a>""", StringComparison.Ordinal);
        var firstGroup = html.IndexOf("""class="filter-group" data-group="region""", StringComparison.Ordinal);
        Assert.InRange(clear, 0, firstGroup);
        Assert.Single(Regex.Matches(html, ">Clear filters</a>"));
        Assert.Matches("""<h2 class="filter-title">Filters</h2>\s*<a href="/staff">Clear filters</a>""", html);
    }

    [Fact]
    public async Task The_page_is_in_danish_when_danish_is_chosen()
    {
        await _browser.Get("/language/da?next=%2Fstaff");
        var html = await _browser.Html(Page);

        Assert.Contains("<h1>Søg skønsmænd</h1>", html);
        Assert.Contains("Navn, e-mail eller postnummer", html);
        Assert.Contains("5 skønsmænd fundet", html);
        Assert.Contains("Kun skønsmænd med ændringer, der venter", html);
    }

    [Fact]
    public async Task An_expert_cannot_search_even_with_filters()
    {
        var expert = new Browser(_app);
        await expert.LogIn(Anna);

        Assert.Equal(HttpStatusCode.Forbidden, (await expert.Get($"{Page}?q=anna&region=7")).StatusCode);
    }

    [Fact]
    public async Task Not_logged_in_with_filters_goes_to_the_front_page()
    {
        AssertRedirect(await new Browser(_app).Get($"{Page}?q=anna"), "/");
    }

    // -------------------------------------------------------------------------
    // Results
    // -------------------------------------------------------------------------

    [Fact]
    public async Task No_filters_shows_every_expert_including_retired_sorted_by_last_then_first_name()
    {
        var html = await _browser.Html(Page);

        Assert.Equal(Everyone, Results(html));
        Assert.Contains("5 experts found", html);
    }

    [Fact]
    public async Task One_result_says_one_expert()
    {
        Assert.Contains("1 expert found", await Search("q=kragh"));
    }

    [Fact]
    public async Task No_results_says_so()
    {
        var html = await Search("q=nobodyatall");

        Assert.Empty(Results(html));
        Assert.Contains("No experts match the filters.", html);
    }

    [Fact]
    public async Task A_result_shows_name_vba_number_postcode_city_status_and_pending_count()
    {
        var html = await _browser.Html(Page);

        var soeren = Row(html, Soeren);
        Assert.Contains("Søren Kragh Østergaard", soeren);
        Assert.Contains("8000 Aarhus C", soeren);
        Assert.Contains("Accepted", soeren);
        Assert.Contains("Standby", Row(html, BoDemo));
        Assert.Contains("Retired", Row(html, RitaRetired));

        Assert.Contains("<th", html);
        foreach (var column in new[] { "Name", "VBA no.", "Postcode and city", "Status", "Pending changes" })
        {
            Assert.Matches($"<th[^>]*>{Regex.Escape(column)}</th>", html);
        }
    }

    [Fact]
    public async Task The_pending_count_counts_only_changes_still_waiting()
    {
        var html = await _browser.Html(Page);

        Assert.Equal("2", PendingPattern().Match(Row(html, AnnaDemo)).Groups[1].Value);
        Assert.Equal("0", PendingPattern().Match(Row(html, BoDemo)).Groups[1].Value); // one approved, one rejected
        Assert.Equal("0", PendingPattern().Match(Row(html, Soeren)).Groups[1].Value);
    }

    [Fact]
    public async Task Results_have_no_links_until_the_expert_page_exists()
    {
        var html = await _browser.Html(Page);

        Assert.All(Rows(html), row => Assert.DoesNotContain("<a ", row));
    }

    [Fact]
    public async Task Results_never_show_notes_email_or_street()
    {
        var html = await _browser.Html(Page);

        Assert.DoesNotContain("NOTE-SECRET", html);
        Assert.DoesNotContain(Anna, html);
        Assert.DoesNotContain("Østerbrogade", html);
    }

    // -------------------------------------------------------------------------
    // The search box
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("anna", new[] { AnnaDemo, AnnaJensen })]
    [InlineData("ANNA", new[] { AnnaDemo, AnnaJensen })]
    [InlineData("emo", new[] { AnnaDemo, BoDemo })]                 // part of the last name (and of the email)
    [InlineData("kragh", new[] { Soeren })]                         // the middle name
    [InlineData("øSTER", new[] { Soeren })]                         // æ/ø/å ignore case too
    [InlineData("SØREN", new[] { Soeren })]
    [InlineData("jensen@exa", new[] { AnnaJensen })]                // part of the email
    [InlineData("@example.com", new[] { AnnaDemo, BoDemo, AnnaJensen, RitaRetired, Soeren })]
    [InlineData("21", new[] { AnnaDemo })]                          // the start of the postcode
    [InlineData("8000", new[] { AnnaJensen, Soeren })]
    [InlineData("100", new string[0])]                              // inside a postcode, not its start
    [InlineData("anna 2100", new[] { AnnaDemo })]                   // every word must match
    [InlineData("anna 8000", new[] { AnnaJensen })]
    [InlineData("  anna   demo  ", new[] { AnnaDemo })]             // extra spaces don't matter
    [InlineData("anna bo", new string[0])]
    [InlineData("%", new string[0])]                                // % and _ are plain characters
    [InlineData("_", new string[0])]
    [InlineData("Østerbrogade", new string[0])]                     // the street is not searched
    public async Task Every_word_must_match_a_name_the_email_or_the_start_of_the_postcode(string q, string[] expected)
    {
        Assert.Equal(expected, Results(await Search($"q={Uri.EscapeDataString(q)}")));
    }

    [Fact]
    public async Task Only_spaces_is_the_same_as_no_search()
    {
        Assert.Equal(Everyone, Results(await Search("q=%20%20%20")));
    }

    [Fact]
    public async Task The_search_text_stays_in_the_box_and_is_encoded()
    {
        var html = await Search($"q={Uri.EscapeDataString("anna <b>")}&region=7");

        Assert.Matches("""<input[^>]*name="q"[^>]*value="anna &lt;b&gt;["]""", html);
        Assert.DoesNotContain("anna <b>", html);
    }

    [Fact]
    public async Task A_search_text_of_254_characters_is_allowed()
    {
        var html = await Search($"q={new string('a', 254)}");

        Assert.DoesNotContain("The search text can be at most 254 characters.", html);
        Assert.Contains("No experts match the filters.", html);
    }

    [Fact]
    public async Task A_search_text_over_254_characters_shows_an_error_and_no_results()
    {
        var html = await Search($"q={new string('a', 255)}");

        Assert.Contains("The search text can be at most 254 characters.", html);
        Assert.Empty(Results(html));
        Assert.DoesNotContain("No experts match the filters.", html);
    }

    // -------------------------------------------------------------------------
    // Sidebar groups: OR inside a group, AND between groups
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("region=7", new[] { AnnaDemo })]
    [InlineData("region=4", new[] { AnnaJensen, Soeren })]
    [InlineData("region=7&region=6", new[] { AnnaDemo, BoDemo })]
    [InlineData("role=36", new[] { BoDemo, Soeren })]
    [InlineData("role=37", new[] { Soeren })]
    [InlineData("role=35&role=37", new[] { AnnaDemo, Soeren })]
    [InlineData("role=44", new string[0])]
    [InlineData("field=EL", new[] { AnnaDemo, Soeren })]
    [InlineData("field=EL&field=Fiber", new[] { AnnaDemo, BoDemo, Soeren })]
    [InlineData("field=Tagd%C3%A6kning", new[] { AnnaJensen })]
    [InlineData("status=0", new[] { RitaRetired })]
    [InlineData("status=1&status=-1", new[] { AnnaDemo, BoDemo, AnnaJensen })]
    [InlineData("pending=1", new[] { AnnaDemo })]
    public async Task Ticks_in_one_group_are_or(string query, string[] expected)
    {
        Assert.Equal(expected, Results(await Search(query)));
    }

    [Theory]
    [InlineData("field=EL&status=2", new[] { Soeren })]
    [InlineData("region=4&q=anna", new[] { AnnaJensen })]
    [InlineData("region=7&region=4&role=36", new[] { Soeren })]
    [InlineData("field=Fiber&pending=1", new string[0])]
    [InlineData("region=7&field=EL&role=35&status=1&pending=1&q=demo", new[] { AnnaDemo })]
    public async Task Groups_are_and(string query, string[] expected)
    {
        Assert.Equal(expected, Results(await Search(query)));
    }

    [Theory]
    [InlineData("region=2")]          // Norge: not one of the five Danish regions
    [InlineData("region=99")]
    [InlineData("region=abc")]
    [InlineData("role=999")]
    [InlineData("role=x")]
    [InlineData("field=Nope")]
    [InlineData("status=7")]
    [InlineData("status=x")]
    [InlineData("pending=yes")]
    public async Task A_value_that_is_not_in_its_list_is_a_bad_request(string query)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _browser.Get($"{Page}?{query}")).StatusCode);
    }

    [Fact]
    public async Task Region_lists_only_the_five_danish_regions_in_vbas_order()
    {
        var region = Group(await _browser.Html(Page), "region");

        Assert.Equal(["7", "8", "5", "4", "6"], Values(region, "region"));
        foreach (var name in new[] { "Hovedstaden", "Sjælland", "Nordjylland", "Midtjylland", "Syddanmark" })
        {
            Assert.Contains(name, region);
        }

        Assert.DoesNotContain("Norge", region);
        Assert.DoesNotContain("Sverige", region);
    }

    [Fact]
    public async Task Status_lists_all_six_statuses_including_retired()
    {
        var status = Group(await _browser.Html(Page), "status");

        Assert.Equal(["-1", "0", "1", "2", "3", "4"], Values(status, "status").Order());
        Assert.Contains("Retired", status);
        Assert.DoesNotContain("show-all", status);
    }

    [Fact]
    public async Task Ticked_boxes_stay_ticked_and_the_heading_counts_them()
    {
        var html = await Search("region=7&region=4&pending=1");

        var region = Group(html, "region");
        Assert.Equal(["7", "4"], Ticked(region, "region"));
        Assert.Contains("2 ticked", region);
        Assert.DoesNotContain("ticked", Group(html, "role"));
        Assert.Equal(["1"], Ticked(Group(html, "pending"), "pending"));
    }

    [Fact]
    public async Task The_phone_filter_summary_counts_every_tick()
    {
        var html = await Search("region=7&region=4&pending=1");

        Assert.Contains("Filters (3 ticked)", html);
        Assert.Contains("<summary>Filters</summary>", await _browser.Html(Page));
    }

    // -------------------------------------------------------------------------
    // Groups with more than 8 values: Role (12) and Field (10)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_big_group_lists_every_value_in_a_scroll_box_in_danish_order()
    {
        // The page is in English, but the order is Danish: Æ, Ø, Å last, and Mentor before Mægler.
        var html = await _browser.Html(Page);

        Assert.Equal(
            ["Beton", "EL", "Fiber", "Maskinanlæg", "Netværk", "Tagdækning", "Varmepumpe", "Ældre bygninger",
             "Øvrige anlæg", "Åbne arealer"],
            Scrolled(Group(html, "field"), "field"));
        Assert.Equal(
            ["44", "38", "34", "37", "41", "39", "40", "36", "35", "42", "45", "43"], // Bisidder … Voldgiftsdommer
            Scrolled(Group(html, "role"), "role"));
        Assert.DoesNotContain("show-all", html);
        Assert.DoesNotContain("Show all", html);
    }

    [Fact]
    public async Task Small_groups_have_no_scroll_box()
    {
        var html = await _browser.Html(Page);

        Assert.Null(Scrolled(Group(html, "region"), "region"));
        Assert.Null(Scrolled(Group(html, "status"), "status"));
    }

    [Fact]
    public async Task Ticked_values_in_a_big_group_come_first()
    {
        var role = Group(await Search("role=43&role=42"), "role");

        // Staderegistrant, Voldgiftsdommer, then the rest in Danish order.
        Assert.Equal(["42", "43", "44", "38", "34", "37", "41", "39", "40", "36", "35", "45"], Scrolled(role, "role"));
    }

    [Fact]
    public async Task More_than_8_ticked_are_all_first_and_still_ticked()
    {
        var query = string.Join('&', new[] { 34, 35, 36, 37, 38, 39, 40, 41, 42 }.Select(id => $"role={id}"));
        var role = Group(await Search(query), "role");

        Assert.Equal(9, Ticked(role, "role").Count);
        Assert.Equal(["44", "45", "43"], Scrolled(role, "role")!.Skip(9)); // the unticked: Bisidder, Tilsynsførende, Voldgiftsdommer
    }

    // -------------------------------------------------------------------------
    // Groups fold open and closed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Groups_without_a_tick_start_closed()
    {
        var html = await _browser.Html(Page);

        foreach (var group in new[] { "region", "role", "field", "status" })
        {
            Assert.DoesNotContain("open", GroupTag(html, group));
        }
    }

    [Fact]
    public async Task A_group_with_a_tick_starts_open_and_the_others_stay_closed()
    {
        var html = await Search("role=37&status=1");

        Assert.Contains("open", GroupTag(html, "role"));
        Assert.Contains("open", GroupTag(html, "status"));
        Assert.DoesNotContain("open", GroupTag(html, "region"));
        Assert.DoesNotContain("open", GroupTag(html, "field"));
    }

    [Fact]
    public async Task The_badge_shows_the_number_ticked_and_only_when_something_is_ticked()
    {
        var html = await Search("field=EL&field=Fiber&field=Beton");

        var badge = Regex.Match(Group(html, "field"), """<span class="badge">.*?</span></span>""", RegexOptions.Singleline).Value;
        Assert.Contains("""<span aria-hidden="true">3</span>""", badge);
        Assert.Contains("3 ticked", badge);
        Assert.DoesNotContain("badge", Group(html, "region"));
    }

    [Fact]
    public async Task Each_group_keeps_a_legend_for_screen_readers()
    {
        var role = Group(await _browser.Html(Page), "role");

        Assert.Contains("""<legend class="visually-hidden">Role</legend>""", role);
    }

    // -------------------------------------------------------------------------
    // Paging: 25 per page
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Five_experts_fit_on_one_page_so_there_are_no_page_links()
    {
        var html = await _browser.Html(Page);

        Assert.Equal(Everyone, Results(html));
        Assert.Equal("", Pager(html));
    }

    [Fact]
    public async Task The_first_page_shows_25_and_says_which_ones()
    {
        AddPagingExperts();
        var html = await _browser.Html(Page);

        Assert.Equal(EveryoneWithPaging[..25], Results(html));
        Assert.Contains("35 experts found", html);
        Assert.Contains("Showing 1–25 of 35", html);
    }

    [Fact]
    public async Task Page_2_shows_the_rest_in_the_same_order()
    {
        AddPagingExperts();
        var html = await Search("page=2");

        Assert.Equal(EveryoneWithPaging[25..], Results(html));
        Assert.Contains("35 experts found", html);
        Assert.Contains("Showing 26–35 of 35", html);
    }

    [Fact]
    public async Task Page_1_written_out_is_the_same_as_no_page()
    {
        AddPagingExperts();

        Assert.Equal(EveryoneWithPaging[..25], Results(await Search("page=1")));
    }

    [Fact]
    public async Task The_page_links_number_every_page_and_mark_the_current_one()
    {
        AddPagingExperts();
        var pager = Pager(await Search("page=2"));

        Assert.Contains("""<a href="/staff" rel="prev">‹ Previous</a>""", pager);
        Assert.Contains("""<a href="/staff">1</a>""", pager);
        Assert.Contains("""<a href="/staff?page=2" aria-current="page">2</a>""", pager);
        Assert.Contains("""<span class="pager-off" aria-disabled="true">Next ›</span>""", pager);
        Assert.Contains("""<nav aria-label="Pages">""", pager);
    }

    [Fact]
    public async Task Previous_is_not_a_link_on_the_first_page()
    {
        AddPagingExperts();
        var pager = Pager(await _browser.Html(Page));

        Assert.Contains("""<span class="pager-off" aria-disabled="true">‹ Previous</span>""", pager);
        Assert.Contains("""<a href="/staff?page=2" rel="next">Next ›</a>""", pager);
        Assert.Contains("""<a href="/staff" aria-current="page">1</a>""", pager);
    }

    [Fact]
    public async Task The_page_links_keep_the_search_and_every_tick()
    {
        AddPagingExperts();
        using (var db = _app.Open())
        {
            db.Execute("INSERT INTO CandidateSkills (CandidateId, Skill) SELECT Id, 'EL' FROM Candidates WHERE Id > 100");
        }

        // The 30 paging experts: "test", field EL, status Active. Søren (EL, Accepted) doesn't match "test".
        var html = await Search("q=test&field=EL&status=1&status=2");
        Assert.Contains("30 experts found", html);

        var links = Links(Pager(html));
        Assert.Contains("/staff?q=test&field=EL&status=1&status=2&page=2", links);
        Assert.Contains("/staff?q=test&field=EL&status=1&status=2", links);
        Assert.All(links, link => Assert.StartsWith("/staff?q=test&field=EL&status=1&status=2", link));
    }

    [Fact]
    public async Task The_page_links_encode_the_search_text()
    {
        AddPagingExperts();
        using (var db = _app.Open())
        {
            db.Execute("UPDATE Candidates SET FirstName = 'Æble & <b>' WHERE Id > 100");
        }

        var links = Links(Pager(await Search($"q={Uri.EscapeDataString("æble &")}")));

        Assert.Contains("/staff?q=%C3%A6ble%20%26&page=2", links);
    }

    [Fact]
    public async Task The_form_has_no_page_field_so_a_new_search_or_tick_goes_to_page_1()
    {
        AddPagingExperts();
        var html = await Search("page=2");

        Assert.DoesNotMatch("""<input[^>]*name="page"[^>]*>""", html);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=abc")]
    [InlineData("page=07")]
    [InlineData("page=%2B2")]   // +2
    [InlineData("page=1.5")]
    [InlineData("page=%201")]  // a space before
    [InlineData("page=3")]      // past the last page (35 experts: 2 pages)
    [InlineData("page=9999999")]
    [InlineData("page=2&q=nobodyatall")] // no results: 1 page
    public async Task A_bad_page_is_a_bad_request(string query)
    {
        AddPagingExperts();

        Assert.Equal(HttpStatusCode.BadRequest, (await _browser.Get($"{Page}?{query}")).StatusCode);
    }

    [Fact]
    public async Task A_page_with_a_search_text_that_is_too_long_is_a_bad_request()
    {
        AddPagingExperts();

        Assert.Equal(HttpStatusCode.BadRequest, (await _browser.Get($"{Page}?page=2&q={new string('a', 255)}")).StatusCode);
    }

    [Fact]
    public async Task The_page_links_are_in_danish_when_danish_is_chosen()
    {
        AddPagingExperts();
        await _browser.Get("/language/da?next=%2Fstaff");
        var pager = Pager(await _browser.Html(Page));

        Assert.Contains("Viser 1–25 af 35", pager);
        Assert.Contains("‹ Forrige", pager);
        Assert.Contains("Næste ›", pager);
        Assert.Contains("""<nav aria-label="Sider">""", pager);
    }

    [Fact]
    public async Task Only_big_groups_have_a_find_box()
    {
        var html = await _browser.Html(Page);

        Assert.Matches("""<input[^>]*class="filter-find"[^>]*aria-label="Find a field["]""", Group(html, "field"));
        Assert.Matches("""<input[^>]*class="filter-find"[^>]*aria-label="Find a role["]""", Group(html, "role"));
        Assert.DoesNotContain("filter-find", Group(html, "region"));
        Assert.DoesNotContain("filter-find", Group(html, "status"));
    }

    [Fact]
    public async Task The_find_box_is_hidden_until_the_script_shows_it_and_is_not_sent_with_the_form()
    {
        var role = Group(await _browser.Html(Page), "role");
        var find = Regex.Match(role, """<input[^>]*class="filter-find"[^>]*>""").Value;

        Assert.Contains(" hidden", find);
        Assert.DoesNotContain("name=", find);
    }
}
