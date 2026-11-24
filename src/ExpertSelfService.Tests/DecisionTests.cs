using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>
/// Story S2, screen S-B: the secretariat approves or rejects a pending change. Approving applies the change
/// (Candidates or CandidateSkills, LastUpdated, one change-log row); rejecting needs a reason the expert sees and
/// changes nothing else. The expert sees "Approved" or "Rejected" + reason on the field (SPEC section 3).
/// </summary>
public sealed class DecisionTests : IAsyncLifetime, IDisposable
{
    private const string Page = "/staff/pending";

    // The pending rows after SetUp, oldest first: Søren's first name, Anna's fields, Søren's middle name.
    private const long SoerenFirst = 2;   // Søren -> Sören, 7 Nov 09:00 UTC
    private const long AnnaSkills = 1;    // + Netværk − Maskinanlæg, 8 Nov 10:00 UTC
    private const long SoerenMiddle = 4;  // Kragh -> (removed), 8 Nov 12:00 UTC
    private const long BoApproved = 3;    // decided already, never listed
    private const long BoRejected = 5;    // decided already, never listed

    private readonly AppFactory _app = new();
    private readonly Browser _browser;

    public DecisionTests() => _browser = new Browser(_app);

    public async Task InitializeAsync()
    {
        SetUp();
        await _browser.UseEnglish();
        AssertRedirect(await _browser.LogIn(Sofie), "/staff");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _app.Dispose();

    // -------------------------------------------------------------------------
    // Test data: added here, not in AppFactory, so no other test class changes.
    // Anna (1) has Varmepumpe, EL and Maskinanlæg (AppFactory).
    // -------------------------------------------------------------------------

    private void SetUp()
    {
        using var db = _app.Open();
        db.Execute(
            """
            INSERT INTO Candidates (Id, FirstName, MiddleName, LastName, Email, Status, RegistrationId, Created) VALUES
                (4, 'Søren', 'Kragh', 'Østergaard', 'soeren.oestergaard@example.com', 2, '900004', '2018-05-01');

            INSERT INTO ss_pending_changes (id, candidate_id, field, old_value, new_value, status, reason, created_at, decided_by, decided_at) VALUES
                (1, 1, 'Skills', 'EL; Maskinanlæg; Varmepumpe', 'EL; Netværk; Varmepumpe', 'pending', NULL, '2026-11-08T10:00:00Z', NULL, NULL),
                (2, 4, 'FirstName', 'Søren', 'Sören', 'pending', NULL, '2026-11-07T09:00:00Z', NULL, NULL),
                (3, 2, 'LastName', 'Demo', 'Dammo', 'approved', NULL, '2026-11-01T10:00:00Z', 1, '2026-11-02T10:00:00Z'),
                (4, 4, 'MiddleName', 'Kragh', NULL, 'pending', NULL, '2026-11-08T12:00:00Z', NULL, NULL),
                (5, 2, 'FirstName', 'Bo', 'Bob', 'rejected', 'Not a name change', '2026-11-03T10:00:00Z', 1, '2026-11-04T10:00:00Z');
            """);
    }

    // -------------------------------------------------------------------------
    // Helpers. The page's HTML contract:
    //   change row:  <tr class="change" data-id="2"> … </tr>
    //   cells:       <td class="vba-no mono">900004</td>, <td class="field">First name</td>, <td class="sent">…</td>
    //   approve:     <form method="post" action="/staff/pending/2/approve"> (+ <input type="hidden" name="q">)
    //   reject:      <details class="reject" …> <form method="post" action="/staff/pending/2/reject">
    //                <textarea name="Reason" …> … <p class="field-error" …>
    //   message:     <p class="decision-message …"> after a decision
    //   top bar:     <a href="/staff">Search experts</a>, <a href="/staff/pending">Pending approvals (3)</a>
    // -------------------------------------------------------------------------

    private sealed record ChangeRow(long Id, string Field, string? OldValue, string? NewValue, string Status, string? Reason, long? DecidedBy, string? DecidedAt);

    private sealed record LogRow(long CandidateId, string ActorType, long ActorId, string Field, string? OldValue, string? NewValue, long? PendingChangeId);

    private sealed record SkillRow(long Id, string Skill);

    private static List<string> Rows(string html) =>
        [.. Regex.Matches(html, """<tr class="change" data-id="\d+"[^>]*>.*?</tr>""", RegexOptions.Singleline).Select(match => match.Value)];

    private static List<long> Ids(string html) =>
        [.. Rows(html).Select(row => long.Parse(Regex.Match(row, @"data-id=""(\d+)""").Groups[1].Value))];

    private static string Row(string html, long id) =>
        Rows(html).Single(row => row.Contains($"data-id=\"{id}\"", StringComparison.Ordinal));

    /// <summary>The visible text: tags removed, entities decoded, spaces collapsed.</summary>
    private static string Text(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();

    private static string Cell(string row, string cssClass)
    {
        var match = Regex.Match(row, $"""<td class="{cssClass}[^"]*"[^>]*>(.*?)</td>""", RegexOptions.Singleline);
        Assert.True(match.Success, $"No cell '{cssClass}'");
        return Text(match.Groups[1].Value);
    }

    private Task<HttpResponseMessage> Approve(long id, params (string Name, string Value)[] fields) =>
        _browser.Post($"{Page}/{id}/approve", Page, fields);

    private Task<HttpResponseMessage> Reject(long id, string reason, params (string Name, string Value)[] fields) =>
        _browser.Post($"{Page}/{id}/reject", Page, [("Reason", reason), .. fields]);

    private ChangeRow Change(long id)
    {
        using var db = _app.Open();
        return db.QuerySingle<ChangeRow>(
            """
            SELECT id AS Id, field AS Field, old_value AS OldValue, new_value AS NewValue, status AS Status,
                   reason AS Reason, decided_by AS DecidedBy, decided_at AS DecidedAt
            FROM ss_pending_changes WHERE id = @id
            """,
            new { id });
    }

    private List<LogRow> Log()
    {
        using var db = _app.Open();
        return [.. db.Query<LogRow>(
            """
            SELECT candidate_id AS CandidateId, actor_type AS ActorType, actor_id AS ActorId, field AS Field,
                   old_value AS OldValue, new_value AS NewValue, pending_change_id AS PendingChangeId
            FROM ss_change_log ORDER BY id
            """)];
    }

    private List<SkillRow> SkillRows(long expertId)
    {
        using var db = _app.Open();
        return [.. db.Query<SkillRow>(
            "SELECT Id, Skill FROM CandidateSkills WHERE CandidateId = @expertId ORDER BY Id", new { expertId })];
    }

    private T Candidate<T>(long id, string column)
    {
        // The column comes from the test itself, never from a request.
        using var db = _app.Open();
        return db.ExecuteScalar<T>($"SELECT {column} FROM Candidates WHERE Id = @id", new { id })!;
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
    public async Task An_expert_cannot_see_the_list()
    {
        var expert = await ExpertLoggedIn(Anna);

        Assert.Equal(HttpStatusCode.Forbidden, (await expert.Get(Page)).StatusCode);
    }

    [Fact]
    public async Task An_expert_cannot_approve_even_with_a_valid_token()
    {
        var expert = await ExpertLoggedIn(Anna);

        var response = await expert.Post($"{Page}/{AnnaSkills}/approve", "/profile");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("pending", Change(AnnaSkills).Status);
    }

    [Fact]
    public async Task Not_logged_in_goes_to_the_front_page()
    {
        AssertRedirect(await new Browser(_app).Get(Page), "/");
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task A_post_without_the_antiforgery_token_is_refused(string action)
    {
        var response = await _browser.Http.PostAsync(
            $"{Page}/{AnnaSkills}/{action}", new FormUrlEncodedContent([KeyValuePair.Create("Reason", "No")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("pending", Change(AnnaSkills).Status);
    }

    // -------------------------------------------------------------------------
    // The list
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_list_shows_only_pending_changes_oldest_first()
    {
        var html = await _browser.Html(Page);

        Assert.Equal([SoerenFirst, AnnaSkills, SoerenMiddle], Ids(html));
        Assert.DoesNotContain("Dammo", html);
        Assert.DoesNotContain("Not a name change", html);
        Assert.Contains("3 changes waiting", Text(html));
    }

    [Fact]
    public async Task A_name_change_shows_the_expert_the_field_and_old_to_new()
    {
        var row = Row(await _browser.Html(Page), SoerenFirst);

        Assert.Contains("Søren Kragh Østergaard", Text(row));
        Assert.Equal("900004", Cell(row, "vba-no"));
        Assert.Equal("First name", Cell(row, "field"));
        Assert.Contains("Søren → Sören", Text(row));
        Assert.Equal("07/11/2026 10:00", Cell(row, "sent")); // 09:00 UTC = 10:00 in Copenhagen
    }

    [Fact]
    public async Task A_removed_middle_name_shows_not_given()
    {
        var row = Row(await _browser.Html(Page), SoerenMiddle);

        Assert.Equal("Middle name", Cell(row, "field"));
        Assert.Contains("Kragh → Not given", Text(row));
    }

    [Fact]
    public async Task A_skills_change_shows_added_and_removed()
    {
        var row = Row(await _browser.Html(Page), AnnaSkills);

        Assert.Equal("Fields of expertise", Cell(row, "field"));
        var text = Text(row);
        Assert.Contains("Added: Netværk", text);
        Assert.Contains("Removed: Maskinanlæg", text);
        Assert.DoesNotContain("Varmepumpe", text); // unchanged fields are not listed
    }

    [Fact]
    public async Task Each_row_has_an_approve_and_a_reject_form()
    {
        var row = Row(await _browser.Html(Page), AnnaSkills);

        Assert.Matches($"""<form method="post" action="{Page}/{AnnaSkills}/approve">""", row);
        Assert.Matches($"""<form method="post" action="{Page}/{AnnaSkills}/reject"[^>]*>""", row);
        Assert.Matches("""<textarea[^>]*name="Reason"[^>]*maxlength="500"[^>]*>""", row);
        Assert.Matches("""<details class="reject"(?![^>]*open)[^>]*>""", row); // closed until there is an error
    }

    [Fact]
    public async Task No_pending_changes_says_so()
    {
        using (var db = _app.Open())
        {
            db.Execute("UPDATE ss_pending_changes SET status = 'approved', decided_by = 1, decided_at = '2026-11-09T08:00:00Z' WHERE status = 'pending'");
        }

        var html = await _browser.Html(Page);

        Assert.Empty(Rows(html));
        Assert.Contains("No changes are waiting.", Text(html));
    }

    [Fact]
    public async Task The_list_is_in_danish_too()
    {
        var danish = new Browser(_app);
        await danish.LogIn(Sofie);
        await danish.Get("/language/da?next=%2F");

        var html = await danish.Html(Page);

        Assert.Contains("3 ændringer venter", Text(html));
        Assert.Equal("Fornavn", Cell(Row(html, SoerenFirst), "field"));
        Assert.Contains("Tilføjet: Netværk", Text(Row(html, AnnaSkills)));
    }

    // -------------------------------------------------------------------------
    // The search box: "Name, email or VBA no.", every word must match
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("søren", new long[] { SoerenFirst, SoerenMiddle })]
    [InlineData("SØREN", new long[] { SoerenFirst, SoerenMiddle })]          // case, also æ/ø/å
    [InlineData("kragh", new long[] { SoerenFirst, SoerenMiddle })]          // middle name
    [InlineData("oestergaard@", new long[] { SoerenFirst, SoerenMiddle })]   // part of the email
    [InlineData("900001", new long[] { AnnaSkills })]                        // the VBA no.
    [InlineData("9000", new long[] { SoerenFirst, AnnaSkills, SoerenMiddle })] // the start of the VBA no.
    [InlineData("0001", new long[] { })]                                     // inside the VBA no., not its start
    [InlineData("anna demo", new long[] { AnnaSkills })]                     // both words
    [InlineData("anna østergaard", new long[] { })]                          // every word must match
    [InlineData("  ", new long[] { SoerenFirst, AnnaSkills, SoerenMiddle })] // only spaces: everything
    public async Task Every_word_must_match_a_name_the_email_or_the_start_of_the_vba_number(string query, long[] expected)
    {
        var html = await _browser.Html($"{Page}?q={Uri.EscapeDataString(query)}");

        Assert.Equal(expected, Ids(html));
    }

    [Fact]
    public async Task The_search_text_stays_in_the_box()
    {
        var html = await _browser.Html($"{Page}?q=s%C3%B8ren");

        Assert.Matches("""<input[^>]*name="q"[^>]*value="søren"[^>]*>""", WebUtility.HtmlDecode(html));
        Assert.Contains("Name, email or VBA no.", Text(html));
    }

    [Fact]
    public async Task No_match_says_so()
    {
        var html = await _browser.Html($"{Page}?q=nobody");

        Assert.Empty(Rows(html));
        Assert.Contains("No waiting changes match the search.", Text(html));
    }

    [Fact]
    public async Task A_search_text_over_254_characters_shows_the_error_and_no_rows()
    {
        var response = await _browser.Get($"{Page}?q={new string('a', 255)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        Assert.Contains("The search text can be at most 254 characters.", Text(html));
        Assert.Empty(Rows(html));
    }

    // -------------------------------------------------------------------------
    // Approve
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Approving_a_name_field_writes_candidates_the_log_and_the_decision()
    {
        AssertRedirect(await Approve(SoerenFirst), Page);

        Assert.Equal("Sören", Candidate<string>(4, "FirstName"));
        Assert.Equal("2026-11-09", Candidate<string>(4, "LastUpdated"));

        var log = Assert.Single(Log());
        Assert.Equal(new LogRow(4, "staff", 1, "FirstName", "Søren", "Sören", SoerenFirst), log);

        var change = Change(SoerenFirst);
        Assert.Equal(("approved", 1L, "2026-11-09T09:00:00Z"), (change.Status, change.DecidedBy, change.DecidedAt));
        Assert.Null(change.Reason);
    }

    [Fact]
    public async Task Approving_a_removed_middle_name_stores_nothing()
    {
        AssertRedirect(await Approve(SoerenMiddle), Page);

        Assert.Null(Candidate<string?>(4, "MiddleName"));
        Assert.Equal(new LogRow(4, "staff", 1, "MiddleName", "Kragh", null, SoerenMiddle), Assert.Single(Log()));
    }

    [Fact]
    public async Task The_log_holds_the_value_candidates_had_when_it_was_approved()
    {
        // The stored old value is from when the expert sent it. If VBA's own system changed the name since,
        // the log must show what was really replaced.
        using (var db = _app.Open())
        {
            db.Execute("UPDATE Candidates SET FirstName = 'Soren' WHERE Id = 4");
        }

        AssertRedirect(await Approve(SoerenFirst), Page);

        Assert.Equal("Soren", Assert.Single(Log()).OldValue);
    }

    [Fact]
    public async Task Approving_skills_changes_only_the_added_and_removed_rows()
    {
        // Anna has Varmepumpe, EL and Maskinanlæg. The change is "+ Netværk − Maskinanlæg".
        var before = SkillRows(1);
        var varmepumpe = before.Single(row => row.Skill == "Varmepumpe").Id;
        var el = before.Single(row => row.Skill == "EL").Id;
        var maskinanlaeg = before.Single(row => row.Skill == "Maskinanlæg").Id;

        AssertRedirect(await Approve(AnnaSkills), Page);

        var after = SkillRows(1);
        Assert.Equal(3, after.Count);
        Assert.Equal(varmepumpe, after.Single(row => row.Skill == "Varmepumpe").Id); // kept its Id
        Assert.Equal(el, after.Single(row => row.Skill == "EL").Id);                 // kept its Id
        Assert.DoesNotContain(after, row => row.Skill == "Maskinanlæg");
        Assert.DoesNotContain(after, row => row.Id == maskinanlaeg);                 // the row is gone
        var netvaerk = after.Single(row => row.Skill == "Netværk");                  // a new row
        Assert.True(netvaerk.Id > before.Max(row => row.Id), "Netværk should be a new row");

        Assert.Equal(
            new LogRow(1, "staff", 1, "Skills", "EL; Maskinanlæg; Varmepumpe", "EL; Netværk; Varmepumpe", AnnaSkills),
            Assert.Single(Log()));
        Assert.Equal("approved", Change(AnnaSkills).Status);
    }

    private void AddSkill(long expertId, string skill)
    {
        using var db = _app.Open();
        db.Execute("INSERT INTO CandidateSkills (CandidateId, Skill) VALUES (@expertId, @skill)", new { expertId, skill });
    }

    private void RemoveSkill(long expertId, string skill)
    {
        using var db = _app.Open();
        db.Execute("DELETE FROM CandidateSkills WHERE CandidateId = @expertId AND Skill = @skill", new { expertId, skill });
    }

    [Fact]
    public async Task Approving_skills_keeps_a_skill_added_after_the_change_was_sent()
    {
        // S-B shows "+ Netværk − Maskinanlæg". Fiber came in after Anna sent the change, so it is not part of what
        // the secretariat approves, and it stays.
        AddSkill(1, "Fiber");
        var fiber = SkillRows(1).Single(row => row.Skill == "Fiber").Id;

        AssertRedirect(await Approve(AnnaSkills), Page);

        var after = SkillRows(1);
        Assert.Equal(fiber, after.Single(row => row.Skill == "Fiber").Id);
        Assert.DoesNotContain(after, row => row.Skill == "Maskinanlæg");
        Assert.Contains(after, row => row.Skill == "Netværk");
        Assert.Equal(4, after.Count);

        // The log shows the list as it really was before and after, not the list the expert sent.
        Assert.Equal(
            new LogRow(1, "staff", 1, "Skills", "EL; Fiber; Maskinanlæg; Varmepumpe", "EL; Fiber; Netværk; Varmepumpe", AnnaSkills),
            Assert.Single(Log()));
    }

    [Fact]
    public async Task Approving_skills_when_a_removed_skill_is_already_gone_still_works()
    {
        RemoveSkill(1, "Maskinanlæg");

        AssertRedirect(await Approve(AnnaSkills), Page);

        Assert.Equal(["EL", "Netværk", "Varmepumpe"], SkillRows(1).Select(row => row.Skill).Order(StringComparer.Ordinal));
        Assert.Equal(
            new LogRow(1, "staff", 1, "Skills", "EL; Varmepumpe", "EL; Netværk; Varmepumpe", AnnaSkills),
            Assert.Single(Log()));
        Assert.Equal("approved", Change(AnnaSkills).Status);
    }

    [Fact]
    public async Task Approving_skills_when_an_added_skill_is_already_there_adds_no_second_row()
    {
        AddSkill(1, "Netværk");

        AssertRedirect(await Approve(AnnaSkills), Page);

        Assert.Single(SkillRows(1), row => row.Skill == "Netværk");
        Assert.DoesNotContain(SkillRows(1), row => row.Skill == "Maskinanlæg");
    }

    [Fact]
    public async Task Approving_skills_leaves_other_experts_alone()
    {
        var bo = SkillRows(2);

        AssertRedirect(await Approve(AnnaSkills), Page);

        Assert.Equal(bo, SkillRows(2));
    }

    // -------------------------------------------------------------------------
    // An approval that changes nothing (review finding #3): the change is decided, but no change-log row is written
    // and LastUpdated stays, because one row is written per changed field (SPEC section 3).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Approving_skills_that_are_already_in_place_writes_no_log_row()
    {
        // The list already matches what Anna sent: Maskinanlæg is gone and Netværk is there.
        RemoveSkill(1, "Maskinanlæg");
        AddSkill(1, "Netværk");
        var skillsBefore = SkillRows(1);

        AssertRedirect(await Approve(AnnaSkills), Page);

        Assert.Equal("approved", Change(AnnaSkills).Status);
        Assert.Empty(Log());
        Assert.Null(Candidate<string?>(1, "LastUpdated"));
        Assert.Equal(skillsBefore, SkillRows(1));
        Assert.Contains("Approved: Anna Demo – Fields of expertise", Text(await _browser.Html(Page)));
    }

    [Theory]
    [InlineData(SoerenFirst, "FirstName", "Sören")]
    [InlineData(SoerenMiddle, "MiddleName", null)]
    public async Task Approving_a_name_that_is_already_in_place_writes_no_log_row(long changeId, string column, string? value)
    {
        using (var db = _app.Open())
        {
            // The column comes from the test's own data, never from a request.
            db.Execute($"UPDATE Candidates SET {column} = @value WHERE Id = 4", new { value });
        }

        AssertRedirect(await Approve(changeId), Page);

        Assert.Equal("approved", Change(changeId).Status);
        Assert.Empty(Log());
        Assert.Null(Candidate<string?>(4, "LastUpdated"));
        Assert.Equal(value, Candidate<string?>(4, column));
        Assert.Contains("Approved:", Text(await _browser.Html(Page)));
    }

    [Fact]
    public async Task After_approving_the_message_says_what_was_approved_and_the_row_is_gone()
    {
        await Approve(AnnaSkills);

        var html = await _browser.Html(Page);

        Assert.Matches("""<p class="decision-message[^"]*"[^>]*>""", html);
        Assert.Contains("Approved: Anna Demo – Fields of expertise", Text(html));
        Assert.Equal([SoerenFirst, SoerenMiddle], Ids(html));
    }

    [Fact]
    public async Task The_search_text_is_kept_after_a_decision()
    {
        AssertRedirect(await Approve(SoerenFirst, ("q", "kragh")), $"{Page}?q=kragh");
    }

    [Fact]
    public async Task A_retired_experts_change_can_still_be_decided()
    {
        using (var db = _app.Open())
        {
            db.Execute("UPDATE Candidates SET Status = 0 WHERE Id = 4");
        }

        AssertRedirect(await Approve(SoerenFirst), Page);

        Assert.Equal("Sören", Candidate<string>(4, "FirstName"));
    }

    // -------------------------------------------------------------------------
    // Reject
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Rejecting_saves_the_reason_and_changes_nothing_else()
    {
        var skillsBefore = SkillRows(1);

        AssertRedirect(await Reject(AnnaSkills, "  Send us your certificate first.  "), Page);

        var change = Change(AnnaSkills);
        Assert.Equal(("rejected", "Send us your certificate first.", 1L, "2026-11-09T09:00:00Z"),
            (change.Status, change.Reason, change.DecidedBy, change.DecidedAt));
        Assert.Equal(skillsBefore, SkillRows(1));
        Assert.Null(Candidate<string?>(1, "LastUpdated"));
        Assert.Empty(Log());
        Assert.Contains("Rejected: Anna Demo – Fields of expertise", Text(await _browser.Html(Page)));
    }

    [Fact]
    public async Task A_reason_may_have_line_breaks()
    {
        // Stored with \n: the browser sends \r\n, but a line break is one character (review finding #2).
        AssertRedirect(await Reject(AnnaSkills, "First line.\r\nSecond line."), Page);

        Assert.Equal("First line.\nSecond line.", Change(AnnaSkills).Reason);
    }

    /// <summary>
    /// <paramref name="letters"/> letters in five lines, joined by the browser's \r\n: the box counts each line break
    /// as one character (maxlength), but the form sends two.
    /// </summary>
    private static string FiveLines(int letters)
    {
        var last = letters - 400;
        return string.Join("\r\n", new string('a', 100), new string('b', 100), new string('c', 100), new string('d', 100), new string('e', last));
    }

    [Fact]
    public async Task A_line_break_counts_as_one_character()
    {
        // 496 letters + 4 line breaks: 500 in the box, 504 sent.
        var reason = FiveLines(496);

        AssertRedirect(await Reject(AnnaSkills, reason), Page);

        var change = Change(AnnaSkills);
        Assert.Equal("rejected", change.Status);
        Assert.Equal(reason.Replace("\r\n", "\n", StringComparison.Ordinal), change.Reason);
        Assert.Equal(500, change.Reason!.Length);
    }

    [Fact]
    public async Task One_more_letter_with_line_breaks_is_too_long()
    {
        // 497 letters + 4 line breaks: 501.
        var response = await Reject(AnnaSkills, FiveLines(497));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The reason can be at most 500 characters.", Text(Row(await Body(response), AnnaSkills)));
        Assert.Equal("pending", Change(AnnaSkills).Status);
    }

    [Fact]
    public async Task A_lone_carriage_return_is_stored_as_a_line_break()
    {
        AssertRedirect(await Reject(AnnaSkills, "First line.\rSecond line."), Page);

        Assert.Equal("First line.\nSecond line.", Change(AnnaSkills).Reason);
    }

    [Theory]
    [InlineData("", "Write a reason for rejecting")]
    [InlineData("   ", "Write a reason for rejecting")]
    [InlineData("Bad\u0007bell", "The reason can't contain control characters.")]
    public async Task A_bad_reason_shows_the_error_in_the_row_and_writes_nothing(string reason, string error)
    {
        var response = await Reject(AnnaSkills, reason);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Row(await Body(response), AnnaSkills);
        Assert.Contains(error, Text(row));
        Assert.Matches("""<details class="reject"[^>]*open[^>]*>""", row); // the box stays open
        Assert.Equal("pending", Change(AnnaSkills).Status);
        Assert.Empty(Log());
    }

    // A bad reason shows S-B again at the POST address, which has no GET. The language switch must return to the
    // list, with its search text, not to that address (Leesha's click-through, 24 Nov: it gave "Page not found").
    [Theory]
    [InlineData(null, "/staff/pending")]
    [InlineData("anna demo", "/staff/pending?q=anna%20demo")]
    public async Task After_a_bad_reason_the_language_switch_returns_to_the_list(string? q, string list)
    {
        var response = q is null ? await Reject(AnnaSkills, "") : await Reject(AnnaSkills, "", ("q", q));
        var html = await Body(response);
        Assert.Contains("Write a reason for rejecting", Text(html));

        var link = LanguageLink(html, "da");
        Assert.Equal($"/language/da?next={Uri.EscapeDataString(list)}", link);

        AssertRedirect(await _browser.Get(link), list);
        var danish = await _browser.Get(list);
        Assert.Equal(HttpStatusCode.OK, danish.StatusCode);
        Assert.Contains("<html lang=\"da\">", await Body(danish));
    }

    [Fact]
    public async Task On_the_list_itself_the_language_switch_returns_to_the_current_address()
    {
        var html = await _browser.Html($"{Page}?q=anna");

        Assert.Equal($"/language/da?next={Uri.EscapeDataString($"{Page}?q=anna")}", LanguageLink(html, "da"));
    }

    [Fact]
    public async Task A_reason_over_500_characters_is_refused_and_kept_in_the_box()
    {
        var reason = new string('x', 501);

        var response = await Reject(AnnaSkills, reason);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Row(await Body(response), AnnaSkills);
        Assert.Contains("The reason can be at most 500 characters.", Text(row));
        Assert.Contains(reason, row);
        Assert.Equal("pending", Change(AnnaSkills).Status);
    }

    [Fact]
    public async Task A_reason_of_exactly_500_characters_is_fine()
    {
        AssertRedirect(await Reject(AnnaSkills, new string('x', 500)), Page);

        Assert.Equal("rejected", Change(AnnaSkills).Status);
    }

    // -------------------------------------------------------------------------
    // Decided once only
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Approving_twice_writes_one_log_row_and_says_already_decided()
    {
        AssertRedirect(await Approve(SoerenFirst), Page);
        AssertRedirect(await Approve(SoerenFirst), Page);

        Assert.Single(Log());
        Assert.Contains("This change has already been decided.", Text(await _browser.Html(Page)));
    }

    [Fact]
    public async Task Rejecting_an_approved_change_does_nothing()
    {
        AssertRedirect(await Approve(AnnaSkills), Page);
        AssertRedirect(await Reject(AnnaSkills, "Too late"), Page);

        var change = Change(AnnaSkills);
        Assert.Equal(("approved", (string?)null), (change.Status, change.Reason));
        Assert.Contains("This change has already been decided.", Text(await _browser.Html(Page)));
    }

    /// <summary>
    /// Plays the losing side of a race the write lock already prevents: the trigger makes the final
    /// "UPDATE … WHERE status = 'pending'" change no row, as if another decision had got there first.
    /// </summary>
    private void DecisionUpdateChangesNoRow()
    {
        using var db = _app.Open();
        db.Execute(
            """
            CREATE TRIGGER lose_the_race BEFORE UPDATE OF status ON ss_pending_changes
            BEGIN
                SELECT RAISE(IGNORE);
            END;
            """);
    }

    [Fact]
    public async Task If_the_approve_update_changes_no_row_nothing_is_written()
    {
        DecisionUpdateChangesNoRow();

        AssertRedirect(await Approve(SoerenFirst), Page);

        Assert.Equal("Søren", Candidate<string>(4, "FirstName"));
        Assert.Null(Candidate<string?>(4, "LastUpdated"));
        Assert.Empty(Log());
        Assert.Equal("pending", Change(SoerenFirst).Status);
        Assert.Contains("This change has already been decided.", Text(await _browser.Html(Page)));
    }

    [Fact]
    public async Task If_the_approve_update_changes_no_row_the_skills_are_not_touched()
    {
        var before = SkillRows(1);
        DecisionUpdateChangesNoRow();

        AssertRedirect(await Approve(AnnaSkills), Page);

        Assert.Equal(before, SkillRows(1));
        Assert.Empty(Log());
    }

    [Fact]
    public async Task If_the_reject_update_changes_no_row_it_says_already_decided()
    {
        DecisionUpdateChangesNoRow();

        AssertRedirect(await Reject(SoerenFirst, "Not a real change"), Page);

        var change = Change(SoerenFirst);
        Assert.Equal(("pending", (string?)null), (change.Status, change.Reason));
        Assert.Contains("This change has already been decided.", Text(await _browser.Html(Page)));
    }

    [Theory]
    [InlineData(BoApproved)]
    [InlineData(BoRejected)]
    public async Task A_change_decided_before_cannot_be_approved(long id)
    {
        var before = Change(id);

        AssertRedirect(await Approve(id), Page);

        Assert.Equal(before, Change(id));
        Assert.Empty(Log());
    }

    [Theory]
    [InlineData("999/approve")]
    [InlineData("999/reject")]
    [InlineData("abc/approve")]
    public async Task An_unknown_change_is_not_found(string path)
    {
        var response = await _browser.Post($"{Page}/{path}", Page, ("Reason", "No"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // A bad reason takes another path in the controller (StatusOf), so unknown and decided are tested on it too.

    [Theory]
    [InlineData("")]
    [InlineData("\u0007")]
    public async Task A_bad_reason_on_an_unknown_change_is_not_found(string reason)
    {
        var response = await Reject(999, reason);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(BoApproved)]
    [InlineData(BoRejected)]
    public async Task A_bad_reason_on_a_decided_change_says_already_decided(long id)
    {
        var before = Change(id);

        AssertRedirect(await Reject(id, ""), Page);

        Assert.Equal(before, Change(id));
        Assert.Contains("This change has already been decided.", Text(await _browser.Html(Page)));
    }

    // -------------------------------------------------------------------------
    // Top bar
    // -------------------------------------------------------------------------

    /// <summary>The top-bar link to <paramref name="href"/>, from its opening tag to its closing tag.</summary>
    private static string NavLink(string html, string href)
    {
        var match = Regex.Match(html, $"""<a href="{href}"(?: aria-current="page")?>.*?</a>""", RegexOptions.Singleline);
        Assert.True(match.Success, $"No top-bar link to {href}");
        return match.Value;
    }

    private static string LogoutForm(string html)
    {
        var match = Regex.Match(html, """<form class="topbar-logout".*?</form>""", RegexOptions.Singleline);
        Assert.True(match.Success, "No log-out form");
        return match.Value;
    }

    [Fact]
    public async Task Staff_pages_link_to_search_and_pending_approvals_with_the_count()
    {
        var search = await _browser.Html("/staff");
        Assert.StartsWith("""<a href="/staff" aria-current="page">""", NavLink(search, "/staff"));
        Assert.Contains("""<span class="label-long">Search experts</span>""", NavLink(search, "/staff"));
        Assert.StartsWith("""<a href="/staff/pending">""", NavLink(search, "/staff/pending"));
        Assert.Contains("""<span class="label-long">Pending approvals (3)</span>""", NavLink(search, "/staff/pending"));

        var pending = await _browser.Html(Page);
        Assert.StartsWith("""<a href="/staff">""", NavLink(pending, "/staff"));
        Assert.StartsWith("""<a href="/staff/pending" aria-current="page">""", NavLink(pending, "/staff/pending"));
    }

    // Routing ignores letter case and a trailing slash, so a typed or bookmarked address like /Staff/ still opens
    // S-A or S-B. The top bar must mark the page there too (review finding #5).
    [Theory]
    [InlineData("/Staff", "/staff", "/staff/pending")]
    [InlineData("/staff/", "/staff", "/staff/pending")]
    [InlineData("/STAFF/", "/staff", "/staff/pending")]
    [InlineData("/Staff/Pending", "/staff/pending", "/staff")]
    [InlineData("/staff/pending/", "/staff/pending", "/staff")]
    public async Task The_top_bar_marks_the_page_whatever_the_letter_case_or_trailing_slash(
        string url, string marked, string notMarked)
    {
        var html = await _browser.Html(url);
        Assert.StartsWith($"""<a href="{marked}" aria-current="page">""", NavLink(html, marked));
        Assert.StartsWith($"""<a href="{notMarked}">""", NavLink(html, notMarked));
    }

    [Fact]
    public async Task The_expert_page_marks_neither_top_bar_link()
    {
        var html = await _browser.Html("/staff/experts/1");
        Assert.StartsWith("""<a href="/staff">""", NavLink(html, "/staff"));
        Assert.StartsWith("""<a href="/staff/pending">""", NavLink(html, "/staff/pending"));
    }

    [Fact]
    public async Task On_a_phone_the_staff_links_have_an_icon_and_a_short_label()
    {
        var html = await _browser.Html(Page);

        var search = NavLink(html, "/staff");
        Assert.Contains("""<svg class="icon""", search);
        Assert.Contains("""<span class="label-short">Search</span>""", search);

        var pending = NavLink(html, "/staff/pending");
        Assert.Contains("""<svg class="icon""", pending);
        Assert.Contains("""<span class="label-short">Pending (3)</span>""", pending);
    }

    [Fact]
    public async Task The_log_out_button_has_an_icon_and_a_label_screen_readers_hear()
    {
        var form = LogoutForm(await _browser.Html(Page));

        Assert.Contains("""<svg class="icon""", form);
        Assert.Contains("""<span class="logout-label">Log out</span>""", form);
    }

    [Fact]
    public async Task The_top_bar_is_in_danish_too()
    {
        var danish = new Browser(_app);
        await danish.LogIn(Sofie);
        await danish.Get("/language/da?next=%2F");

        var html = await danish.Html(Page);

        Assert.Contains("""<span class="label-long">Søg skønsmænd</span>""", NavLink(html, "/staff"));
        Assert.Contains("""<span class="label-short">Søg</span>""", NavLink(html, "/staff"));
        Assert.Contains("""<span class="label-long">Afventer godkendelse (3)</span>""", NavLink(html, "/staff/pending"));
        Assert.Contains("""<span class="label-short">Afventer (3)</span>""", NavLink(html, "/staff/pending"));
        Assert.Contains("""<span class="logout-label">Log ud</span>""", LogoutForm(html));
    }

    [Fact]
    public async Task The_count_goes_down_after_a_decision()
    {
        await Reject(SoerenMiddle, "Keep the middle name.");

        Assert.Contains("Pending approvals (2)", await _browser.Html("/staff"));
    }

    [Fact]
    public async Task Expert_pages_have_no_staff_links()
    {
        var expert = await ExpertLoggedIn(Anna);

        var html = await expert.Html("/profile");

        Assert.DoesNotContain("Pending approvals", html);
        Assert.DoesNotContain("""href="/staff""", html);

        // The expert's phone bar: logo, DA · EN and the log-out icon.
        var form = LogoutForm(html);
        Assert.Contains("""<svg class="icon""", form);
        Assert.Contains("""<span class="logout-label">Log out</span>""", form);
    }

    // -------------------------------------------------------------------------
    // Approve and Reject side by side; Clear next to Search
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Approve_and_reject_sit_together_in_one_row()
    {
        var row = Row(await _browser.Html(Page), AnnaSkills);

        Assert.Matches(
            """<div class="decision-actions">\s*<form method="post" action="[^"]*/approve">.*<details class="reject""",
            Regex.Replace(row, @"\s+", " "));
    }

    [Theory]
    [InlineData("s%C3%B8ren")]
    [InlineData("nobody")]  // no match: Clear still helps
    public async Task Clear_shows_next_to_search_after_a_search_with_text(string query)
    {
        var html = await _browser.Html($"{Page}?q={query}");

        Assert.Matches(
            """<button type="submit">Search</button>\s*<a class="search-clear" href="/staff/pending">Clear</a>""", html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?q=")]
    [InlineData("?q=%20%20")]
    public async Task Clear_is_not_shown_without_search_text(string query)
    {
        Assert.DoesNotContain("search-clear", await _browser.Html($"{Page}{query}"));
    }

    [Fact]
    public async Task Clear_is_in_danish_too()
    {
        var danish = new Browser(_app);
        await danish.LogIn(Sofie);
        await danish.Get("/language/da?next=%2F");

        Assert.Contains("""<a class="search-clear" href="/staff/pending">Ryd</a>""", await danish.Html($"{Page}?q=anna"));
    }

    // -------------------------------------------------------------------------
    // The expert sees the outcome
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Demo_steps_5_and_6_anna_adds_a_field_sofie_approves_anna_sees_approved()
    {
        using (var db = _app.Open())
        {
            db.Execute("DELETE FROM ss_pending_changes WHERE candidate_id = 1");
        }

        var anna = await ExpertLoggedIn(Anna);
        AssertRedirect(
            await anna.Post("/profile/skills", "/profile/skills",
                ("Skills", "Varmepumpe"), ("Skills", "EL"), ("Skills", "Maskinanlæg"), ("Skills", "Netværk")),
            "/profile");
        Assert.Contains("Pending approval", await anna.Html("/profile"));

        var id = _app.Count("SELECT MAX(id) FROM ss_pending_changes WHERE candidate_id = 1");
        AssertRedirect(await Approve(id), Page);

        var profile = WebUtility.HtmlDecode(await anna.Html("/profile"));
        var skills = Regex.Match(profile, """<section[^>]*\bid="skills"[^>]*>(.*?)</section>""", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("Approved", skills);
        Assert.Contains("Netværk", skills);
        Assert.DoesNotContain("Pending approval", skills);
    }

    [Fact]
    public async Task After_approval_the_name_screen_shows_approved_and_stays_editable()
    {
        using (var db = _app.Open())
        {
            db.Execute(
                """
                INSERT INTO ss_pending_changes (id, candidate_id, field, old_value, new_value, status, created_at)
                VALUES (10, 1, 'LastName', 'Demo', 'Demo-Holm', 'pending', '2026-11-09T08:00:00Z')
                """);
        }

        AssertRedirect(await Approve(10), Page);

        var anna = await ExpertLoggedIn(Anna);
        var html = WebUtility.HtmlDecode(await anna.Html("/profile/name"));
        Assert.Contains("Approved", html);
        Assert.Matches("""<input[^>]*name="LastName"[^>]*value="Demo-Holm"[^>]*>""", html);
    }

    [Fact]
    public async Task After_approval_the_skills_screen_shows_approved_above_the_tick_boxes()
    {
        AssertRedirect(await Approve(AnnaSkills), Page);

        var anna = await ExpertLoggedIn(Anna);
        var html = await anna.Html("/profile/skills");
        Assert.Contains("Approved", html);
        Assert.Matches("""<input[^>]*name="Skills"[^>]*value="Netværk"[^>]*checked""", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task After_rejection_the_expert_sees_rejected_and_the_reason()
    {
        AssertRedirect(await Reject(AnnaSkills, "Send us your certificate first."), Page);

        var anna = await ExpertLoggedIn(Anna);
        var html = WebUtility.HtmlDecode(await anna.Html("/profile"));
        Assert.Contains("Rejected", html);
        Assert.Contains("Send us your certificate first.", html);
    }
}
