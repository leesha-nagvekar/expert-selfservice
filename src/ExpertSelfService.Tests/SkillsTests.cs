using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using ExpertSelfService.Services;
using Microsoft.Extensions.DependencyInjection;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>
/// Story E7, screen X9: the expert picks fields of expertise from VBA's list (Fags). It goes to approval: one
/// ss_pending_changes row with the whole old and new list, and CandidateSkills, LastUpdated and the change log
/// stay untouched until S2 (SPEC sections 3 and 6).
/// </summary>
public sealed class SkillsTests : IAsyncLifetime, IDisposable
{
    private const string Page = "/profile/skills";
    private const string Now = "2026-11-09T09:00:00Z"; // AppFactory's clock

    private const string Title = "Edit fields of expertise";
    private const string Intro = "Changes to your fields of expertise must be approved by the secretariat.";
    private const string SendButton = "Send for approval";
    private const string ChooseOne = "Choose at least one field of expertise.";
    private const string NoLongerListed = "No longer on VBA's list";

    // Anna's fields in AppFactory, in Danish order.
    private const string AnnasList = "EL; Maskinanlæg; Varmepumpe";

    // Every Fags name in AppFactory.
    private static readonly string[] AllFags =
        ["EL", "Fiber", "Maskinanlæg", "Netværk", "Varmepumpe", "Ældre bygninger", "Øvrige anlæg", "Åbne arealer"];

    private readonly AppFactory _app = new();
    private readonly Browser _browser;

    public SkillsTests() => _browser = new Browser(_app);

    public async Task InitializeAsync()
    {
        await _browser.UseEnglish();
        AssertRedirect(await _browser.LogIn(Anna), "/profile");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _app.Dispose();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Task<HttpResponseMessage> Send(Browser browser, params string[] skills) =>
        browser.Post(Page, Page, [.. skills.Select(skill => ("Skills", skill))]);

    private Task<HttpResponseMessage> Send(params string[] skills) => Send(_browser, skills);

    private sealed record PendingRow(
        long CandidateId, string Field, string? OldValue, string? NewValue, string Status,
        string? Reason, string CreatedAt, long? DecidedBy, string? DecidedAt);

    private List<PendingRow> Pending()
    {
        using var db = _app.Open();
        return db.Query<PendingRow>(
            """
            SELECT candidate_id AS CandidateId, field AS Field, old_value AS OldValue, new_value AS NewValue,
                   status AS Status, reason AS Reason, created_at AS CreatedAt,
                   decided_by AS DecidedBy, decided_at AS DecidedAt
            FROM ss_pending_changes ORDER BY id
            """).ToList();
    }

    /// <summary>A row as S2 (or an earlier send) would have left it, written straight into the table.</summary>
    private void InsertState(string? newValue, string status, string? reason = null)
    {
        using var db = _app.Open();
        db.Execute(
            """
            INSERT INTO ss_pending_changes (candidate_id, field, old_value, new_value, status, reason, created_at, decided_by, decided_at)
            VALUES (1, 'Skills', NULL, @newValue, @status, @reason, '2026-11-01T08:00:00Z',
                    CASE WHEN @status = 'pending' THEN NULL ELSE 1 END,
                    CASE WHEN @status = 'pending' THEN NULL ELSE '2026-11-02T08:00:00Z' END)
            """,
            new { newValue, status, reason });
    }

    private List<string> SkillsOf(long id)
    {
        using var db = _app.Open();
        return [.. db.Query<string>("SELECT Skill FROM CandidateSkills WHERE CandidateId = @id ORDER BY Id", new { id })];
    }

    private void GiveAnna(string skill)
    {
        using var db = _app.Open();
        db.Execute("INSERT INTO CandidateSkills (CandidateId, Skill) VALUES (1, @skill)", new { skill });
    }

    private string? LastUpdated(long id = 1)
    {
        using var db = _app.Open();
        return db.QuerySingle<string?>("SELECT LastUpdated FROM Candidates WHERE Id = @id", new { id });
    }

    private int ChangeLogRows() => _app.Count("SELECT COUNT(*) FROM ss_change_log");

    private static string Decoded(string html) => WebUtility.HtmlDecode(html);

    private static string Section(string html, string id)
    {
        var match = Regex.Match(html, $"""<section[^>]*\bid="{id}"[^>]*>(.*?)</section>""", RegexOptions.Singleline);
        Assert.True(match.Success, $"No section with id=\"{id}\"");
        return Decoded(match.Groups[1].Value);
    }

    private static string Fieldset(string html)
    {
        var match = Regex.Match(Decoded(html), """<fieldset[^>]*>(.*?)</fieldset>""", RegexOptions.Singleline);
        Assert.True(match.Success, "No fieldset");
        return match.Value;
    }

    private static string CheckboxPattern(string name) =>
        $"""<input(?=[^>]*\btype="checkbox")(?=[^>]*\bname="Skills")(?=[^>]*\bvalue="{Regex.Escape(name)}")[^>]*>""";

    /// <summary>The tick box for this name, and whether it is ticked.</summary>
    private static bool? Ticked(string html, string name)
    {
        var match = Regex.Match(Decoded(html), CheckboxPattern(name));
        return match.Success ? Regex.IsMatch(match.Value, @"\bchecked\b") : null;
    }

    private static bool HasCheckboxes(string html) =>
        Regex.IsMatch(html, """<input(?=[^>]*\btype="checkbox")(?=[^>]*\bname="Skills")[^>]*>""");

    private static void AssertChooseOneError(string html, string message = ChooseOne)
    {
        var decoded = Decoded(html);
        Assert.Matches("""<fieldset(?=[^>]*\baria-describedby="skills-error")[^>]*>""", decoded);
        Assert.Matches($"""<p class="field-error" id="skills-error">\s*{Regex.Escape(message)}\s*</p>""", decoded);
    }

    private void AssertNothingWritten()
    {
        Assert.Empty(Pending());
        Assert.Equal(["Varmepumpe", "EL", "Maskinanlæg"], SkillsOf(1));
        Assert.Null(LastUpdated());
        Assert.Equal(0, ChangeLogRows());
    }

    // -------------------------------------------------------------------------
    // The form
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_profile_has_a_link_to_edit_fields_of_expertise()
    {
        var skills = Section(await _browser.Html("/profile"), "skills");

        Assert.Matches($"""<a[^>]*href="/profile/skills"[^>]*>\s*{Title}\s*</a>""", skills);
    }

    [Fact]
    public async Task The_form_has_a_tick_box_for_every_field_on_vbas_list_with_annas_ticked()
    {
        var response = await _browser.Get(Page);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);

        Assert.Contains($"<h1>{Title}</h1>", html);
        Assert.Contains(Intro, Decoded(html));
        Assert.Matches("""<legend>\s*Fields of expertise\s*</legend>""", Fieldset(html));
        foreach (var name in AllFags)
        {
            Assert.Equal(name is "EL" or "Maskinanlæg" or "Varmepumpe", Ticked(html, name));
        }

        Assert.Equal(AllFags.Length, Regex.Matches(html, @"type=""checkbox""").Count);
        Assert.Matches($"""<button[^>]*type="submit"[^>]*>\s*{SendButton}\s*</button>""", html);
        Assert.Contains("""href="/profile">Back to your profile</a>""", html);
    }

    [Fact]
    public async Task Each_tick_box_has_its_own_label()
    {
        var html = Decoded(await _browser.Html(Page));
        var boxes = Regex.Matches(html, """<input(?=[^>]*\btype="checkbox")[^>]*\bid="([^"]+)"[^>]*>""");

        Assert.Equal(AllFags.Length, boxes.Count); // not true by default on an empty page
        foreach (Match box in boxes)
        {
            Assert.Matches($"""<label[^>]*\bfor="{box.Groups[1].Value}"[^>]*>""", html);
        }
    }

    [Fact]
    public async Task The_form_is_in_danish_too()
    {
        var danish = new Browser(_app); // Danish is the default
        await danish.LogIn(Anna);
        var html = Decoded(await danish.Html(Page));

        Assert.Contains("<h1>Rediger fagområder</h1>", html);
        Assert.Contains("Ændringer af dine fagområder skal godkendes af sekretariatet.", html);
        Assert.Matches("""<legend>\s*Fagområder\s*</legend>""", html);
        Assert.Matches("""<button[^>]*type="submit"[^>]*>\s*Send til godkendelse\s*</button>""", html);
        Assert.Matches("""<a[^>]*href="/profile/skills"[^>]*>\s*Rediger fagområder\s*</a>""", Decoded(await danish.Html("/profile")));
    }

    [Fact]
    public async Task A_field_no_longer_on_vbas_list_is_shown_ticked_and_marked()
    {
        GiveAnna("Tømrer"); // VBA has since removed it from Fags

        var html = await _browser.Html(Page);

        Assert.True(Ticked(html, "Tømrer"));
        var box = Regex.Match(Decoded(html), CheckboxPattern("Tømrer")).Value;
        var id = Regex.Match(box, @"\bid=""([^""]+)""").Groups[1].Value;
        Assert.Matches($"""<label[^>]*\bfor="{id}"[^>]*>\s*Tømrer\s*<span[^>]*>\s*{NoLongerListed}\s*</span>\s*</label>""", Decoded(html));
        Assert.DoesNotContain(NoLongerListed, Regex.Replace(Decoded(html), "Tømrer.*?</label>", "", RegexOptions.Singleline));
    }

    // -------------------------------------------------------------------------
    // Sending: one pending row with the whole lists, nothing else
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Adding_a_field_writes_one_pending_row_with_both_lists_and_touches_nothing_else()
    {
        var response = await Send("EL", "Maskinanlæg", "Varmepumpe", "Fiber");

        AssertRedirect(response, "/profile");
        Assert.Equal(
            new PendingRow(1, "Skills", AnnasList, "EL; Fiber; Maskinanlæg; Varmepumpe", "pending", null, Now, null, null),
            Assert.Single(Pending()));
        Assert.Equal(["Varmepumpe", "EL", "Maskinanlæg"], SkillsOf(1)); // CandidateSkills untouched
        Assert.Null(LastUpdated());
        Assert.Equal(0, ChangeLogRows());
    }

    [Fact]
    public async Task The_profile_shows_pending_approval_and_keeps_the_current_fields()
    {
        await Send("EL", "Fiber");

        var skills = Regex.Replace(Section(await _browser.Html("/profile"), "skills"), @"\s+", " ");
        Assert.Matches("""<svg[^>]*aria-hidden="true"[^>]*>.*?</svg> Pending approval""", skills);
        Assert.Contains("EL; Fiber", skills);
        Assert.Contains("<li>Maskinanlæg</li>", skills); // the current list is still shown
        Assert.Contains("<li>Varmepumpe</li>", skills);
    }

    [Fact]
    public async Task Removing_a_field_writes_the_shorter_list()
    {
        await Send("EL", "Varmepumpe");

        var row = Assert.Single(Pending());
        Assert.Equal((AnnasList, "EL; Varmepumpe"), (row.OldValue, row.NewValue));
    }

    [Theory]
    [InlineData("EL", "Maskinanlæg", "Varmepumpe")]
    [InlineData("Varmepumpe", "EL", "Maskinanlæg")]
    [InlineData("EL", "EL", "Maskinanlæg", "Varmepumpe", "Varmepumpe")]
    public async Task The_same_fields_in_any_order_or_twice_are_no_change(params string[] skills)
    {
        AssertRedirect(await Send(skills), "/profile");

        AssertNothingWritten();
        Assert.DoesNotContain("Pending approval", Section(await _browser.Html("/profile"), "skills"));
    }

    [Fact]
    public async Task The_list_is_stored_in_danish_order_even_from_the_english_page()
    {
        // English sorts Å with A, Æ as AE and Ø with O. Danish puts Æ, Ø and Å last, in that order.
        await Send("Åbne arealer", "Øvrige anlæg", "Ældre bygninger", "Fiber", "EL");

        Assert.Equal("EL; Fiber; Ældre bygninger; Øvrige anlæg; Åbne arealer", Assert.Single(Pending()).NewValue);
    }

    [Fact]
    public async Task The_old_list_is_stored_in_danish_order_too_and_without_duplicates()
    {
        GiveAnna("EL");          // a duplicate row, as old data may have
        GiveAnna("Øvrige anlæg");

        await Send("EL");

        Assert.Equal(("EL; Maskinanlæg; Varmepumpe; Øvrige anlæg", "EL"), (Pending()[0].OldValue, Pending()[0].NewValue));
    }

    [Fact]
    public async Task Each_row_stores_the_id_of_the_expert_in_the_session()
    {
        await Send("EL");
        var bo = new Browser(_app);
        await bo.LogIn(Bo);
        await Send(bo, "Fiber", "Netværk");

        var rows = Pending();
        Assert.Equal(2, rows.Count);
        Assert.Equal((1L, AnnasList, "EL"), (rows[0].CandidateId, rows[0].OldValue, rows[0].NewValue));
        Assert.Equal((2L, "Fiber", "Fiber; Netværk"), (rows[1].CandidateId, rows[1].OldValue, rows[1].NewValue));
        Assert.Equal(["Fiber"], SkillsOf(2));
    }

    [Fact]
    public async Task A_field_no_longer_on_the_list_can_be_kept()
    {
        GiveAnna("Tømrer");

        AssertRedirect(await Send("EL", "Tømrer"), "/profile");

        var row = Assert.Single(Pending());
        Assert.Equal(("EL; Maskinanlæg; Tømrer; Varmepumpe", "EL; Tømrer"), (row.OldValue, row.NewValue));
    }

    [Fact]
    public async Task A_field_no_longer_on_the_list_can_be_removed()
    {
        GiveAnna("Tømrer");

        await Send("EL", "Maskinanlæg", "Varmepumpe");

        Assert.Equal("EL; Maskinanlæg; Varmepumpe", Assert.Single(Pending()).NewValue);
    }

    [Fact]
    public async Task A_rejected_change_shows_the_reason_and_a_new_send_works()
    {
        InsertState("EL; Fiber", "rejected", "Send us proof of your fibre work first.");

        var html = await _browser.Html(Page);
        Assert.True(Ticked(html, "EL"));
        var state = Decoded(html);
        Assert.Contains("Rejected", state);
        Assert.Contains("Send us proof of your fibre work first.", state);
        Assert.True(state.IndexOf("Rejected", StringComparison.Ordinal) < state.IndexOf("<fieldset", StringComparison.Ordinal), "The reason sits above the tick boxes");

        AssertRedirect(await Send("EL", "Netværk"), "/profile");
        var row = Pending()[^1];
        Assert.Equal(("EL; Netværk", "pending"), (row.NewValue, row.Status));
    }

    // -------------------------------------------------------------------------
    // A pending change locks the screen, and the server refuses a send
    // -------------------------------------------------------------------------

    [Fact]
    public async Task While_a_change_is_pending_there_are_no_tick_boxes_and_no_button()
    {
        InsertState("EL; Fiber", "pending");

        var response = await _browser.Get(Page);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);

        Assert.False(HasCheckboxes(html), "A pending list must not be editable");
        Assert.DoesNotContain(SendButton, html);
        var decoded = Decoded(html);
        Assert.Contains("Pending approval", decoded);
        Assert.Contains("EL; Fiber", decoded);
        Assert.Contains("<li>Maskinanlæg</li>", decoded); // the current fields are shown
        Assert.Matches("""<a[^>]*href="/profile"[^>]*>""", html);
    }

    [Fact]
    public async Task A_send_while_a_change_is_pending_writes_nothing()
    {
        InsertState("EL; Fiber", "pending");
        var before = Assert.Single(Pending());

        // The screen has no form, so this is a hand-made post (or a second tab). The token comes from /profile/name.
        var response = await _browser.Post(Page, "/profile/name", ("Skills", "Netværk"));

        AssertRedirect(response, "/profile");
        Assert.Equal(before, Assert.Single(Pending()));
        Assert.Equal(["Varmepumpe", "EL", "Maskinanlæg"], SkillsOf(1));
    }

    /// <summary>
    /// The controller's redirect is the first guard; this is the second. Two tabs (or a double click) can both pass
    /// the controller before either writes. The service must still refuse, because it reads the state again inside
    /// its write transaction. So it is called directly here, after the other change was written.
    /// </summary>
    [Fact]
    public void The_service_refuses_a_send_when_a_change_became_pending_after_the_page_was_loaded()
    {
        var service = _app.Services.GetRequiredService<SkillService>();
        InsertState("EL; Fiber", "pending"); // written by the other tab
        var before = Assert.Single(Pending());

        Assert.Equal(SendResult.NothingChanged, service.Send(1, ["Netværk"]));

        Assert.Equal(before, Assert.Single(Pending()));
        Assert.Equal(["Varmepumpe", "EL", "Maskinanlæg"], SkillsOf(1));
    }

    [Fact]
    public async Task A_send_with_nothing_ticked_while_pending_also_writes_nothing()
    {
        InsertState("EL; Fiber", "pending");

        AssertRedirect(await _browser.Post(Page, "/profile/name"), "/profile");

        Assert.Single(Pending());
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("rejected")]
    public async Task When_the_newest_row_is_approved_the_screen_is_editable_again(string olderStatus)
    {
        InsertState("EL; Fiber", olderStatus, olderStatus == "rejected" ? "No proof." : null);
        InsertState(AnnasList, "approved"); // newer: S2 approved it

        var html = await _browser.Html(Page);
        Assert.True(HasCheckboxes(html));
        Assert.DoesNotContain("Pending approval", Decoded(html));
        Assert.DoesNotContain("Rejected", Decoded(html));

        AssertRedirect(await Send("EL"), "/profile");
        Assert.Equal(("EL", "pending"), (Pending()[^1].NewValue, Pending()[^1].Status));
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Nothing_ticked_shows_the_error_and_writes_nothing()
    {
        var response = await Send();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        AssertChooseOneError(html);
        Assert.All(AllFags, name => Assert.False(Ticked(html, name))); // what was sent is shown: nothing ticked
        AssertNothingWritten();
    }

    [Fact]
    public async Task The_error_is_in_danish_too()
    {
        var danish = new Browser(_app);
        await danish.LogIn(Anna);

        AssertChooseOneError(await Body(await Send(danish)), "Vælg mindst ét fagområde.");
    }

    [Fact]
    public async Task An_old_field_alone_is_still_a_choice()
    {
        GiveAnna("Tømrer");

        AssertRedirect(await Send("Tømrer"), "/profile");

        Assert.Equal("Tømrer", Assert.Single(Pending()).NewValue);
    }

    [Theory]
    [InlineData("Tømrer")]            // not in Fags, and not one of Anna's
    [InlineData("Fri tekst")]
    [InlineData("el")]                // case counts: the Fags name is "EL"
    [InlineData("EL ")]
    [InlineData("")]
    [InlineData("EL; Fiber")]         // a whole list in one value
    public async Task A_name_that_is_not_on_the_list_is_refused(string name)
    {
        var response = await Send("EL", name);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Page not found", await Body(response));
        AssertNothingWritten();
    }

    [Fact]
    public async Task Another_experts_old_field_is_not_a_choice()
    {
        using (var db = _app.Open())
        {
            db.Execute("INSERT INTO CandidateSkills (CandidateId, Skill) VALUES (2, 'Tømrer')");
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await Send("EL", "Tømrer")).StatusCode);
        AssertNothingWritten();
    }

    // -------------------------------------------------------------------------
    // Security
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Id")]
    [InlineData("expertId")]
    [InlineData("CandidateId")]
    public async Task An_id_in_the_form_is_ignored(string name)
    {
        AssertRedirect(await _browser.Post(Page, Page, ("Skills", "EL"), (name, "2")), "/profile");

        Assert.Equal(1, Assert.Single(Pending()).CandidateId);
        Assert.Equal(["Fiber"], SkillsOf(2));
    }

    [Fact]
    public async Task An_id_in_the_path_gives_page_not_found()
    {
        var response = await _browser.Get("/profile/skills/2");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await Body(response));
    }

    [Fact]
    public async Task Staff_cannot_open_or_post_the_form()
    {
        var staff = new Browser(_app);
        await staff.UseEnglish();
        await staff.LogIn(Sofie);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Get(Page)).StatusCode);
        var post = await staff.Post(Page, "/staff", ("Skills", "EL"));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Not_logged_in_goes_to_the_front_page()
    {
        AssertRedirect(await new Browser(_app).Get(Page), "/");
    }

    [Fact]
    public async Task A_post_without_the_antiforgery_token_is_refused()
    {
        var response = await _browser.Http.PostAsync(
            Page, new FormUrlEncodedContent([KeyValuePair.Create("Skills", "EL")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNothingWritten();
    }
}
