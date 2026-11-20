using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>
/// Story S3, screen S-C: the secretariat sees one expert's details (read-only), their pending changes and their
/// change log, newest first: when, who, field, old value, new value. Nobody can edit or delete a log entry.
/// Step A: the page, the details, the log and the name links from S-A and S-B.
/// </summary>
public sealed class ExpertPageTests : IAsyncLifetime, IDisposable
{
    private const string AnnaPage = "/staff/experts/1";
    private const string NotGiven = "Not given";

    private readonly AppFactory _app = new();
    private readonly Browser _browser;

    public ExpertPageTests() => _browser = new Browser(_app);

    public async Task InitializeAsync()
    {
        await _browser.UseEnglish();
        AssertRedirect(await _browser.LogIn(Sofie), "/staff");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _app.Dispose();

    // -------------------------------------------------------------------------
    // Helpers. The page's HTML contract:
    //   sections:   <section id="details">, <section id="vba">, <section id="pending">, <section id="log">
    //   log row:    <tr class="log-entry"> <td class="when">, <td class="who">, <td class="field">,
    //               <td class="old">, <td class="new"> </tr>
    //   pending:    <tr class="change" data-id="N"> (as on S-B)
    // -------------------------------------------------------------------------

    private sealed record Entry(string When, string Who, string Field, string Old, string New);

    /// <summary>The HTML inside &lt;section id="..."&gt;. Fails if the section is missing.</summary>
    private static string Section(string html, string id)
    {
        var match = Regex.Match(html, $"""<section[^>]*\bid="{id}"[^>]*>(.*?)</section>""", RegexOptions.Singleline);
        Assert.True(match.Success, $"No section with id=\"{id}\"");
        return match.Groups[1].Value;
    }

    /// <summary>The visible text: tags removed, entities decoded, spaces collapsed.</summary>
    private static string Text(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();

    private static string Cell(string row, string cssClass)
    {
        var match = Regex.Match(row, $"""<td class="{cssClass}[^"]*"[^>]*>(.*?)</td>""", RegexOptions.Singleline);
        Assert.True(match.Success, $"No cell '{cssClass}'");
        return Text(match.Groups[1].Value);
    }

    private static List<Entry> Entries(string html) =>
        [.. Regex.Matches(Section(html, "log"), """<tr class="log-entry"[^>]*>.*?</tr>""", RegexOptions.Singleline)
            .Select(match => new Entry(
                Cell(match.Value, "when"), Cell(match.Value, "who"), Cell(match.Value, "field"),
                Cell(match.Value, "old"), Cell(match.Value, "new")))];

    private void Log(long expertId, string actorType, long actorId, string field, string? oldValue, string? newValue, string createdAt)
    {
        using var db = _app.Open();
        db.Execute(
            """
            INSERT INTO ss_change_log (candidate_id, actor_type, actor_id, field, old_value, new_value, created_at)
            VALUES (@expertId, @actorType, @actorId, @field, @oldValue, @newValue, @createdAt)
            """,
            new { expertId, actorType, actorId, field, oldValue, newValue, createdAt });
    }

    private async Task<Browser> ExpertLoggedIn(string email)
    {
        var expert = new Browser(_app);
        await expert.UseEnglish();
        AssertRedirect(await expert.LogIn(email), "/profile");
        return expert;
    }

    // -------------------------------------------------------------------------
    // Access
    // -------------------------------------------------------------------------

    [Fact]
    public async Task An_expert_cannot_open_the_page()
    {
        var expert = await ExpertLoggedIn(Anna);

        Assert.Equal(HttpStatusCode.Forbidden, (await expert.Get(AnnaPage)).StatusCode);
    }

    [Fact]
    public async Task Not_logged_in_goes_to_the_front_page()
    {
        AssertRedirect(await new Browser(_app).Get(AnnaPage), "/");
    }

    [Theory]
    [InlineData("/staff/experts/999")]
    [InlineData("/staff/experts/abc")]
    [InlineData("/staff/experts/0")]
    public async Task An_unknown_expert_is_not_found(string url)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _browser.Get(url)).StatusCode);
    }

    [Fact]
    public async Task A_retired_expert_can_be_opened()
    {
        // The expert side refuses Retired experts; the secretariat owns the record and still sees it.
        var response = await _browser.Get("/staff/experts/3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        Assert.Contains("Rita Retired", Text(html));
        Assert.Contains("Retired", Text(Section(html, "vba")));
    }

    // -------------------------------------------------------------------------
    // Details
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_heading_is_the_experts_name()
    {
        var html = await _browser.Html(AnnaPage);

        Assert.Matches("""<h1[^>]*>\s*Anna Demo\s*</h1>""", html);
    }

    [Fact]
    public async Task The_details_show_every_group_with_the_experts_values()
    {
        var details = Text(Section(await _browser.Html(AnnaPage), "details"));

        foreach (var value in new[]
        {
            "Anna", "Demo", "Østerbrogade 64", "2100 København Ø", "+45 20 11 22 33", "33 44 55 66",
            "anna.demo@example.com", "Holm Rådgivning ApS", "12345674", "Rådgivende ingeniør",
            "EL", "Maskinanlæg", "Varmepumpe",
        })
        {
            Assert.Contains(value, details);
        }

        // Anna has no home phone.
        Assert.Contains(NotGiven, details);
    }

    [Fact]
    public async Task The_skills_are_exactly_the_experts_own_sorted()
    {
        // Exactly Anna's three, in order: an extra skill (another expert's) or a lost sort fails here.
        var details = Section(await _browser.Html(AnnaPage), "details");
        var list = Regex.Match(details, """<h3>Fields of expertise</h3>\s*<ul[^>]*>(.*?)</ul>""", RegexOptions.Singleline);
        Assert.True(list.Success, "No skills list under 'Fields of expertise'");

        var skills = Regex.Matches(list.Groups[1].Value, "<li>(.*?)</li>", RegexOptions.Singleline)
            .Select(match => Text(match.Groups[1].Value));

        Assert.Equal(["EL", "Maskinanlæg", "Varmepumpe"], skills);
    }

    [Fact]
    public async Task Set_by_vba_shows_the_number_created_status_and_roles()
    {
        var vba = Text(Section(await _browser.Html(AnnaPage), "vba"));

        Assert.Contains("900001", vba);
        Assert.Contains("Active", vba);
        Assert.Contains("Skønsmand", vba);
        Assert.Contains("2015", vba);
    }

    [Fact]
    public async Task An_expert_without_details_shows_not_given()
    {
        // Rita has no street, postcode, phones or company.
        var details = Text(Section(await _browser.Html("/staff/experts/3"), "details"));

        Assert.Contains(NotGiven, details);
        Assert.DoesNotContain("København", details);
    }

    [Fact]
    public async Task The_page_has_no_edit_links()
    {
        var html = await _browser.Html(AnnaPage);

        Assert.DoesNotContain("href=\"/profile", html);
        Assert.DoesNotContain("Edit ", Text(html));
    }

    [Fact]
    public async Task Columns_that_must_never_be_shown_are_not_shown()
    {
        using (var db = _app.Open())
        {
            db.Execute(
                """
                UPDATE Candidates SET
                    Notes = 'MARKER-NOTES', Fax = 'MARKER-FAX', Website = 'MARKER-WEBSITE', Address = 'MARKER-ADDRESS',
                    FirstName2 = 'MARKER-FIRSTNAME2', LastName2 = 'MARKER-LASTNAME2',
                    FirstNameOld = 'MARKER-FIRSTNAMEOLD', LastNameOld = 'MARKER-LASTNAMEOLD',
                    LastAccessed = 'MARKER-LASTACCESSED', Stat_Provins = 'MARKER-PROVINS'
                WHERE Id = 1
                """);
        }

        Assert.DoesNotContain("MARKER-", await _browser.Html(AnnaPage));
    }

    [Fact]
    public async Task Another_experts_details_never_show()
    {
        var html = await _browser.Html(AnnaPage);

        Assert.DoesNotContain("Vestergade 8", html);
        Assert.DoesNotContain("bo.demo@example.com", html);
        Assert.DoesNotContain("Fiber", html); // Bo's only skill
    }

    // -------------------------------------------------------------------------
    // Pending changes (read-only in step A)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_experts_pending_changes_are_listed()
    {
        using (var db = _app.Open())
        {
            db.Execute(
                """
                INSERT INTO ss_pending_changes (id, candidate_id, field, old_value, new_value, status, reason, created_at) VALUES
                    (1, 1, 'Skills', 'EL; Maskinanlæg; Varmepumpe', 'EL; Netværk; Varmepumpe', 'pending', NULL, '2026-11-08T10:00:00Z'),
                    (2, 2, 'FirstName', 'Bo', 'Bob', 'pending', NULL, '2026-11-08T11:00:00Z'),
                    (3, 1, 'LastName', 'Demo', 'Dammo', 'rejected', 'Not a name change', '2026-11-01T10:00:00Z');
                """);
        }

        var pending = Section(await _browser.Html(AnnaPage), "pending");

        Assert.Single(Regex.Matches(pending, """<tr class="change" data-id="1"[^>]*>"""));
        Assert.DoesNotContain("data-id=\"2\"", pending); // another expert's
        Assert.DoesNotContain("data-id=\"3\"", pending); // already decided
        Assert.Contains("Added: Netværk", Text(pending));
        Assert.Contains("Removed: Maskinanlæg", Text(pending));
        Assert.Contains("08/11/2026 11:00", Text(pending));
    }

    [Fact]
    public async Task Without_pending_changes_it_says_so()
    {
        Assert.Contains("No changes are waiting.", Text(Section(await _browser.Html(AnnaPage), "pending")));
    }

    // -------------------------------------------------------------------------
    // Change log
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_log_shows_when_who_field_old_and_new_newest_first()
    {
        Log(1, "expert", 1, "PhoneMobile", null, "+45 20 11 22 33", "2026-11-07T13:12:00Z");
        Log(1, "staff", 1, "LastName", "Demo", "Holm", "2026-11-09T09:00:00Z");
        Log(1, "expert", 1, "ZipcodeId", "8000 Aarhus C", "2100 København Ø", "2026-11-07T13:12:00Z");

        var entries = Entries(await _browser.Html(AnnaPage));

        Assert.Equal(
            [
                new Entry("09/11/2026 10:00", "Sofie Staff (secretariat)", "Last name", "Demo", "Holm"),
                // Same time: the one written last comes first.
                new Entry("07/11/2026 14:12", "The expert", "Postcode and city", "8000 Aarhus C", "2100 København Ø"),
                new Entry("07/11/2026 14:12", "The expert", "Mobile phone", NotGiven, "+45 20 11 22 33"),
            ],
            entries);
    }

    [Theory]
    [InlineData("FirstName", "First name")]
    [InlineData("MiddleName", "Middle name")]
    [InlineData("LastName", "Last name")]
    [InlineData("Street", "Street and number")]
    [InlineData("ZipcodeId", "Postcode and city")]
    [InlineData("PhoneMobile", "Mobile phone")]
    [InlineData("PhoneWork", "Work phone")]
    [InlineData("PhoneHome", "Home phone")]
    [InlineData("Email", "Email address")]
    [InlineData("Company", "Company")]
    [InlineData("Cvr", "CVR number")]
    [InlineData("Designation", "Job title")]
    [InlineData("Skills", "Fields of expertise")]
    [InlineData("SomethingNew", "SomethingNew")]
    public async Task Every_logged_field_has_its_label(string field, string label)
    {
        Log(1, "expert", 1, field, "a", "b", "2026-11-09T08:00:00Z");

        Assert.Equal(label, Assert.Single(Entries(await _browser.Html(AnnaPage))).Field);
    }

    [Fact]
    public async Task The_log_shows_only_this_experts_entries()
    {
        Log(1, "expert", 1, "Street", "Old street", "Østerbrogade 64", "2026-11-08T08:00:00Z");
        Log(2, "expert", 2, "Street", "Bos old street", "Vestergade 8", "2026-11-08T09:00:00Z");

        var entries = Entries(await _browser.Html(AnnaPage));

        Assert.Equal("Old street", Assert.Single(entries).Old);
    }

    [Fact]
    public async Task Without_entries_the_log_says_so()
    {
        Assert.Contains("No changes yet.", Text(Section(await _browser.Html(AnnaPage), "log")));
    }

    [Fact]
    public async Task An_address_saved_by_the_expert_shows_up_in_the_log()
    {
        var expert = await ExpertLoggedIn(Anna);
        AssertRedirect(
            await expert.Post(
                "/profile/address",
                "/profile/address",
                ("Street", "Banegårdspladsen 1"),
                ("Postcode", "2100"),
                ("PhoneMobile", "+45 20 11 22 33"),
                ("PhoneWork", "33 44 55 66"),
                ("PhoneHome", "")),
            "/profile");

        var entry = Assert.Single(Entries(await _browser.Html(AnnaPage)));

        Assert.Equal(new Entry("09/11/2026 10:00", "The expert", "Street and number", "Østerbrogade 64", "Banegårdspladsen 1"), entry);
    }

    [Fact]
    public async Task An_approval_on_pending_approvals_shows_up_in_the_log()
    {
        using (var db = _app.Open())
        {
            db.Execute(
                """
                INSERT INTO ss_pending_changes (id, candidate_id, field, old_value, new_value, status, created_at)
                VALUES (1, 1, 'LastName', 'Demo', 'Holm', 'pending', '2026-11-08T10:00:00Z')
                """);
        }

        AssertRedirect(await _browser.Post("/staff/pending/1/approve", "/staff/pending"), "/staff/pending");

        var entry = Assert.Single(Entries(await _browser.Html(AnnaPage)));
        Assert.Equal(new Entry("09/11/2026 10:00", "Sofie Staff (secretariat)", "Last name", "Demo", "Holm"), entry);
    }

    // -------------------------------------------------------------------------
    // Change log paging: 10 per page, the same Pager and ?page=N rules as S-A
    //   pager:  <div class="pager"> <p class="pager-range">Showing 1–10 of 23</p> … rel="prev" / rel="next" …
    // -------------------------------------------------------------------------

    /// <summary>Entries "value 1" … "value N" for Anna, one minute apart, so "value N" is the newest.</summary>
    private void ManyEntries(int count)
    {
        for (var n = 1; n <= count; n++)
        {
            Log(1, "expert", 1, "Street", null, $"value {n}", $"2026-11-01T{n / 60:00}:{n % 60:00}:00Z");
        }
    }

    private static List<string> NewValues(string html) => [.. Entries(html).Select(entry => entry.New)];

    [Fact]
    public async Task The_log_shows_10_entries_per_page_newest_first()
    {
        ManyEntries(23);

        var html = await _browser.Html(AnnaPage);

        Assert.Equal([.. Enumerable.Range(14, 10).Reverse().Select(n => $"value {n}")], NewValues(html));
        Assert.Contains("Showing 1–10 of 23", Text(Section(html, "log")));
    }

    [Fact]
    public async Task The_last_page_shows_the_oldest_entries()
    {
        ManyEntries(23);

        var html = await _browser.Html($"{AnnaPage}?page=3");

        Assert.Equal(["value 3", "value 2", "value 1"], NewValues(html));
        Assert.Contains("Showing 21–23 of 23", Text(Section(html, "log")));
    }

    [Fact]
    public async Task Previous_and_next_link_to_the_neighbouring_pages()
    {
        ManyEntries(23);

        var first = Section(await _browser.Html(AnnaPage), "log");
        var middle = Section(await _browser.Html($"{AnnaPage}?page=2"), "log");
        var last = Section(await _browser.Html($"{AnnaPage}?page=3"), "log");

        Assert.DoesNotContain("rel=\"prev\"", first);
        Assert.Contains($"""<a href="{AnnaPage}?page=2" rel="next">""", first);
        // Page 1 has no ?page, as on S-A.
        Assert.Contains($"""<a href="{AnnaPage}" rel="prev">""", middle);
        Assert.Contains($"""<a href="{AnnaPage}?page=3" rel="next">""", middle);
        Assert.Contains($"""<a href="{AnnaPage}?page=2" rel="prev">""", last);
        Assert.DoesNotContain("rel=\"next\"", last);
        Assert.Contains($"""<a href="{AnnaPage}?page=2" aria-current="page">2</a>""", middle);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public async Task Ten_or_fewer_entries_have_no_pager(int count)
    {
        ManyEntries(count);

        var log = Section(await _browser.Html(AnnaPage), "log");

        Assert.Equal(count, Regex.Matches(log, """<tr class="log-entry""").Count);
        Assert.DoesNotContain("class=\"pager", log);
    }

    [Fact]
    public async Task Eleven_entries_make_two_pages()
    {
        ManyEntries(11);

        var html = await _browser.Html($"{AnnaPage}?page=2");

        Assert.Equal(["value 1"], NewValues(html));
        Assert.Contains("Showing 11–11 of 11", Text(Section(html, "log")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    public async Task No_page_or_page_1_is_the_first_page(string page)
    {
        ManyEntries(12);

        var html = await _browser.Html($"{AnnaPage}?page={page}");

        Assert.Equal("value 12", NewValues(html)[0]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("07")]
    [InlineData("-1")]
    [InlineData("+2")]
    [InlineData("1.5")]
    [InlineData("abc")]
    [InlineData("9999999")]
    public async Task A_page_that_is_not_plain_digits_is_a_bad_request(string page)
    {
        ManyEntries(23);

        Assert.Equal(HttpStatusCode.BadRequest, (await _browser.Get($"{AnnaPage}?page={page}")).StatusCode);
    }

    [Theory]
    [InlineData(23, 4)]
    [InlineData(10, 2)]
    [InlineData(0, 2)]
    public async Task A_page_past_the_end_is_a_bad_request(int entries, int page)
    {
        ManyEntries(entries);

        Assert.Equal(HttpStatusCode.BadRequest, (await _browser.Get($"{AnnaPage}?page={page}")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_expert_is_not_found_whatever_the_page()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _browser.Get("/staff/experts/999?page=abc")).StatusCode);
    }

    [Fact]
    public async Task The_details_and_pending_changes_show_on_every_page()
    {
        ManyEntries(23);

        var html = await _browser.Html($"{AnnaPage}?page=2");

        Assert.Contains("Østerbrogade 64", Text(Section(html, "details")));
        Assert.Contains("No changes are waiting.", Text(Section(html, "pending")));
    }

    [Fact]
    public async Task The_pager_is_in_danish_when_danish_is_chosen()
    {
        ManyEntries(23);
        await _browser.Get("/language/da?next=%2F");

        var log = Text(Section(await _browser.Html(AnnaPage), "log"));

        Assert.Contains("Viser 1–10 af 23", log);
        Assert.Contains("Næste", log);
    }

    // -------------------------------------------------------------------------
    // Nobody can edit or delete an entry
    // -------------------------------------------------------------------------

    [Fact]
    public void The_database_refuses_to_change_a_log_entry()
    {
        Log(1, "expert", 1, "Street", "a", "b", "2026-11-08T08:00:00Z");
        using var db = _app.Open();

        var error = Assert.Throws<SqliteException>(() => db.Execute("UPDATE ss_change_log SET new_value = 'x'"));

        Assert.Contains("append-only", error.Message);
        Assert.Equal("b", db.ExecuteScalar<string>("SELECT new_value FROM ss_change_log"));
    }

    [Fact]
    public void The_database_refuses_to_delete_a_log_entry()
    {
        Log(1, "expert", 1, "Street", "a", "b", "2026-11-08T08:00:00Z");
        using var db = _app.Open();

        var error = Assert.Throws<SqliteException>(() => db.Execute("DELETE FROM ss_change_log"));

        Assert.Contains("append-only", error.Message);
        Assert.Equal(1, db.ExecuteScalar<int>("SELECT COUNT(*) FROM ss_change_log"));
    }

    [Fact]
    public async Task The_log_has_no_forms_or_buttons()
    {
        Log(1, "expert", 1, "Street", "a", "b", "2026-11-08T08:00:00Z");

        var log = Section(await _browser.Html(AnnaPage), "log");

        Assert.DoesNotContain("<form", log);
        Assert.DoesNotContain("<button", log);
        Assert.DoesNotContain("<input", log);
    }

    // -------------------------------------------------------------------------
    // Name links from S-A and S-B
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Names_on_search_experts_link_to_the_expert_page()
    {
        var html = await _browser.Html("/staff?q=anna");

        Assert.Matches("""<td[^>]*class="result-name"[^>]*>\s*<a href="/staff/experts/1(\?[^"]*)?">Anna Demo</a>""", html);
    }

    [Fact]
    public async Task Names_on_pending_approvals_link_to_the_expert_page()
    {
        using (var db = _app.Open())
        {
            db.Execute(
                """
                INSERT INTO ss_pending_changes (id, candidate_id, field, old_value, new_value, status, created_at)
                VALUES (1, 2, 'FirstName', 'Bo', 'Bob', 'pending', '2026-11-08T10:00:00Z')
                """);
        }

        var html = await _browser.Html("/staff/pending");

        Assert.Matches("""<td class="expert result-name"[^>]*>\s*<a href="/staff/experts/2(\?[^"]*)?">Bo Demo</a>""", html);
    }

    // -------------------------------------------------------------------------
    // Language
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_page_is_in_danish_when_danish_is_chosen()
    {
        Log(1, "expert", 1, "Street", "a", "b", "2026-11-08T08:00:00Z");
        await _browser.Get("/language/da?next=%2F");

        var html = await _browser.Html(AnnaPage);

        Assert.Contains("Ændringslog", Text(Section(html, "log")));
        Assert.Contains("Skønsmanden", Text(Section(html, "log")));
        Assert.Contains("Vej og nummer", Text(Section(html, "log")));
    }
}
