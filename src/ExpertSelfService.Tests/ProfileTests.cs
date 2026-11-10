using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>Story E2, screen X4: the expert sees their own profile, read-only. SPEC section 4.</summary>
public sealed class ProfileTests : IAsyncLifetime, IDisposable
{
    private const string PendingApproval = "Pending approval";
    private const string Rejected = "Rejected";
    private const string NotGiven = "Not given";

    // A new app and database per test, because some tests change the data.
    private readonly AppFactory _app = new();
    private readonly Browser _browser;

    public ProfileTests() => _browser = new Browser(_app);

    public Task InitializeAsync() => _browser.UseEnglish();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _app.Dispose();

    private async Task<string> ProfileOf(string email)
    {
        AssertRedirect(await _browser.LogIn(email), "/profile");
        return await _browser.Html("/profile");
    }

    /// <summary>
    /// The HTML inside &lt;section id="..."&gt;, with entities decoded: Razor writes "+" as "&amp;#x2B;",
    /// which the browser shows as "+". Fails if the section is missing.
    /// </summary>
    private static string Section(string html, string id)
    {
        var match = Regex.Match(html, $"""<section[^>]*\bid="{id}"[^>]*>(.*?)</section>""", RegexOptions.Singleline);
        Assert.True(match.Success, $"No section with id=\"{id}\"");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static void AssertMono(string html, string value) =>
        Assert.Matches($"""class="mono"[^>]*>\s*{Regex.Escape(value)}\s*<""", html);

    private void Pending(long expertId, string field, string newValue, string status = "pending", string? reason = null)
    {
        using var db = _app.Open();
        db.Execute(
            """
            INSERT INTO ss_pending_changes (candidate_id, field, old_value, new_value, status, reason, created_at, decided_by, decided_at)
            VALUES (@expertId, @field, 'old', @newValue, @status, @reason, '2026-11-09T08:00:00Z',
                    CASE WHEN @status = 'pending' THEN NULL ELSE 1 END,
                    CASE WHEN @status = 'pending' THEN NULL ELSE '2026-11-09T08:30:00Z' END)
            """,
            new { expertId, field, newValue, status, reason });
    }

    // -------------------------------------------------------------------------
    // What is shown
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Every_field_is_shown_in_its_section()
    {
        var html = await ProfileOf(Anna);

        var name = Section(html, "name");
        Assert.Contains("Anna", name);
        Assert.Contains("Demo", name);

        var address = Section(html, "address");
        Assert.Contains("Østerbrogade 64", address);
        Assert.Contains("2100 København Ø", address);
        Assert.Contains("+45 20 11 22 33", address);
        Assert.Contains("33 44 55 66", address);

        Assert.Contains(Anna, Section(html, "email"));

        var company = Section(html, "company");
        Assert.Contains("Holm Rådgivning ApS", company);
        Assert.Contains("12345674", company);
        Assert.Contains("Rådgivende ingeniør", company);

        var skills = Section(html, "skills");
        Assert.Contains("EL", skills);
        Assert.Contains("Maskinanlæg", skills);
        Assert.Contains("Varmepumpe", skills);
    }

    [Fact]
    public async Task Vba_number_created_date_status_and_roles_are_marked_set_by_vba()
    {
        var vba = Section(await ProfileOf(Anna), "vba");

        Assert.Contains("Set by VBA", vba);
        Assert.Contains("900001", vba);
        Assert.Contains("02/03/2015", vba);
        Assert.Contains("Active", vba);
        Assert.Contains("Skønsmand", vba);
    }

    [Fact]
    public async Task Vba_number_and_cvr_are_in_the_mono_font()
    {
        var html = await ProfileOf(Anna);

        AssertMono(Section(html, "vba"), "900001");
        AssertMono(Section(html, "company"), "12345674");
    }

    [Fact]
    public async Task The_page_has_no_form_fields_because_it_is_read_only()
    {
        var main = Regex.Match(await ProfileOf(Anna), "<main>(.*?)</main>", RegexOptions.Singleline).Groups[1].Value;

        Assert.DoesNotContain("<input", main);
        Assert.DoesNotContain("<form", main);
    }

    [Fact]
    public async Task Status_is_an_icon_and_a_word()
    {
        var vba = Section(await ProfileOf(Anna), "vba");

        Assert.Matches("""<svg[^>]*aria-hidden="true"[^>]*>.*?</svg>\s*Active""", Regex.Replace(vba, @"\s+", " "));
    }

    [Fact]
    public async Task Standby_shows_the_end_date()
    {
        var vba = Section(await ProfileOf(Bo), "vba");

        Assert.Contains("Standby", vba);
        Assert.Contains("until 31/03/2027", vba);
        Assert.Contains("<svg", vba);
    }

    [Fact]
    public async Task Dates_follow_the_language()
    {
        await _browser.LogIn(Bo);
        await _browser.Get("/language/da?next=%2Fprofile");

        var vba = Section(await _browser.Html("/profile"), "vba");

        Assert.Contains("31.03.2027", vba);
        Assert.Contains("17.06.2019", vba);
    }

    [Fact]
    public async Task Missing_values_say_not_given()
    {
        var html = await ProfileOf(Bo);

        Assert.Contains(NotGiven, Section(html, "address")); // no phones
        Assert.Contains(NotGiven, Section(html, "company")); // no company, CVR or job title
        Assert.DoesNotContain(NotGiven, Section(html, "name"));
    }

    [Fact]
    public async Task Fields_of_expertise_are_sorted()
    {
        var skills = Section(await ProfileOf(Anna), "skills");

        var el = skills.IndexOf("EL", StringComparison.Ordinal);
        var machines = skills.IndexOf("Maskinanlæg", StringComparison.Ordinal);
        var heatPump = skills.IndexOf("Varmepumpe", StringComparison.Ordinal);
        Assert.True(el < machines && machines < heatPump, "Expected EL, Maskinanlæg, Varmepumpe in that order");
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

        var html = await ProfileOf(Anna);

        Assert.DoesNotContain("MARKER-", html);
    }

    [Fact]
    public async Task The_page_is_in_danish_when_danish_is_chosen()
    {
        await _browser.LogIn(Anna);
        await _browser.Get("/language/da?next=%2Fprofile");

        var html = await _browser.Html("/profile");

        Assert.Contains("Adresse og telefon", Section(html, "address"));
        Assert.Contains("Fastsat af VBA", Section(html, "vba"));
        Assert.Contains("Aktiv", Section(html, "vba"));
    }

    // -------------------------------------------------------------------------
    // Pending approval and rejected (SPEC section 3)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_pending_name_change_shows_pending_approval_and_the_new_value()
    {
        Pending(1, "FirstName", "Annie");

        var html = await ProfileOf(Anna);

        var name = Section(html, "name");
        Assert.Contains(PendingApproval, name);
        Assert.Contains("Annie", name);
        Assert.DoesNotContain(PendingApproval, Section(html, "skills"));
    }

    [Fact]
    public async Task A_pending_skills_change_shows_in_fields_of_expertise()
    {
        Pending(1, "Skills", "EL; Fiber");

        var html = await ProfileOf(Anna);

        var skills = Section(html, "skills");
        Assert.Contains(PendingApproval, skills);
        Assert.Contains("EL; Fiber", skills);
        Assert.DoesNotContain(PendingApproval, Section(html, "name"));
    }

    [Fact]
    public async Task A_rejected_change_shows_rejected_and_the_reason()
    {
        Pending(1, "LastName", "Demosen", status: "rejected", reason: "Send us the marriage certificate first");

        var name = Section(await ProfileOf(Anna), "name");

        Assert.Contains(Rejected, name);
        Assert.Contains("Demosen", name);
        Assert.Contains("Send us the marriage certificate first", name);
    }

    [Fact]
    public async Task A_newer_pending_change_replaces_an_older_rejection()
    {
        Pending(1, "LastName", "Demosen", status: "rejected", reason: "Old reason");
        Pending(1, "LastName", "Holm");

        var name = Section(await ProfileOf(Anna), "name");

        Assert.Contains(PendingApproval, name);
        Assert.Contains("Holm", name);
        Assert.DoesNotContain(Rejected, name);
        Assert.DoesNotContain("Old reason", name);
    }

    [Fact]
    public async Task An_approved_change_shows_no_state()
    {
        Pending(1, "FirstName", "Annie", status: "approved");

        var html = await ProfileOf(Anna);

        Assert.DoesNotContain(PendingApproval, html);
        Assert.DoesNotContain(Rejected, html);
        Assert.DoesNotContain("Annie", html);
    }

    [Fact]
    public async Task Another_experts_pending_change_never_shows()
    {
        Pending(2, "FirstName", "Bobby");
        Pending(2, "LastName", "Lundsen", status: "rejected", reason: "Bo's reason");

        var html = await ProfileOf(Anna);

        Assert.DoesNotContain("Bobby", html);
        Assert.DoesNotContain("Bo's reason", html);
        Assert.DoesNotContain(PendingApproval, html);
    }

    // -------------------------------------------------------------------------
    // Only your own data (SPEC section 2, demo step 7)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task An_expert_sees_only_their_own_data()
    {
        var html = await ProfileOf(Anna);

        Assert.DoesNotContain("Vestergade 8", html);
        Assert.DoesNotContain("900002", html);
        Assert.DoesNotContain(Bo, html);
    }

    [Fact]
    public async Task An_id_in_the_path_gives_page_not_found()
    {
        await _browser.LogIn(Anna);

        var response = await _browser.Get("/profile/2");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await Body(response));
    }

    [Theory]
    [InlineData("/profile?id=2")]
    [InlineData("/profile?expert_id=2")]
    [InlineData("/profile?expertId=2")]
    public async Task An_id_in_the_query_is_ignored(string url)
    {
        await _browser.LogIn(Anna);

        var html = await _browser.Html(url);

        Assert.Contains("Østerbrogade 64", html);
        Assert.DoesNotContain("Vestergade 8", html);
    }

    [Fact]
    public async Task An_expert_set_to_retired_during_the_session_is_logged_out()
    {
        await _browser.LogIn(Anna);
        using (var db = _app.Open())
        {
            db.Execute("UPDATE Candidates SET Status = 0 WHERE Id = 1");
        }

        AssertRedirect(await _browser.Get("/profile"), "/");

        // Really logged out: the front page shows the email form, not a redirect to /profile.
        var front = await _browser.Get("/");
        Assert.Equal(HttpStatusCode.OK, front.StatusCode);
        Assert.Contains("action=\"/login\"", await Body(front));
    }

    [Fact]
    public async Task An_expert_removed_during_the_session_is_logged_out()
    {
        await _browser.LogIn(Anna);
        using (var db = _app.Open())
        {
            db.Execute(
                """
                DELETE FROM CandidateSkills WHERE CandidateId = 1;
                DELETE FROM CandidateTags WHERE CandidateId = 1;
                DELETE FROM ss_expert_company WHERE candidate_id = 1;
                DELETE FROM Candidates WHERE Id = 1;
                """);
        }

        AssertRedirect(await _browser.Get("/profile"), "/");
        Assert.Equal(HttpStatusCode.OK, (await _browser.Get("/")).StatusCode);
    }
}
