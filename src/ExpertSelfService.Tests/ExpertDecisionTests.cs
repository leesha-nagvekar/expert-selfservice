using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>
/// Story S3, screen S-C, step B: the secretariat approves or rejects an expert's pending change on the expert's own
/// page. The S2 rules apply unchanged (DecisionService); the one new rule is that the change must belong to the
/// expert in the URL. A change decided on S-C is gone from S-B, and the other way round; the top-bar count follows.
/// </summary>
public sealed class ExpertDecisionTests : IAsyncLifetime, IDisposable
{
    private const string AnnaPage = "/staff/experts/1";
    private const string BoPage = "/staff/experts/2";
    private const string PendingPage = "/staff/pending";

    // After SetUp: two pending changes (one each), and one of Anna's already decided.
    private const long AnnaSkills = 1;   // Anna (1): + Netværk − Maskinanlæg, pending
    private const long BoFirst = 2;      // Bo (2): Bo -> Bob, pending
    private const long AnnaRejected = 3; // Anna (1): decided already, never listed

    private readonly AppFactory _app = new();
    private readonly Browser _browser;

    public ExpertDecisionTests() => _browser = new Browser(_app);

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
    // Anna (1) has Varmepumpe, EL and Maskinanlæg; Bo (2) has Fiber (AppFactory).
    // -------------------------------------------------------------------------

    private void SetUp()
    {
        using var db = _app.Open();
        db.Execute(
            """
            INSERT INTO ss_pending_changes (id, candidate_id, field, old_value, new_value, status, reason, created_at, decided_by, decided_at) VALUES
                (1, 1, 'Skills', 'EL; Maskinanlæg; Varmepumpe', 'EL; Netværk; Varmepumpe', 'pending', NULL, '2026-11-08T10:00:00Z', NULL, NULL),
                (2, 2, 'FirstName', 'Bo', 'Bob', 'pending', NULL, '2026-11-08T11:00:00Z', NULL, NULL),
                (3, 1, 'LastName', 'Demo', 'Dammo', 'rejected', 'Not a name change', '2026-11-01T10:00:00Z', 1, '2026-11-02T10:00:00Z');
            """);
    }

    // -------------------------------------------------------------------------
    // Helpers. S-C's decision contract (the same cell as S-B, other form URLs):
    //   change row:  <section id="pending"> … <tr class="change" data-id="1"> … </tr>
    //   approve:     <form method="post" action="/staff/experts/1/changes/1/approve">
    //   reject:      <details class="reject" …> <form method="post" action="/staff/experts/1/changes/1/reject">
    //                <textarea name="Reason" …> … <p class="field-error" …>
    //   message:     <p class="decision-message …"> after a decision
    // -------------------------------------------------------------------------

    private sealed record ChangeRow(long Id, string Status, string? Reason, long? DecidedBy);

    private sealed record LogRow(long CandidateId, string ActorType, long ActorId, string Field, string? OldValue, string? NewValue, long? PendingChangeId);

    private static string ApproveUrl(long expertId, long changeId) => $"/staff/experts/{expertId}/changes/{changeId}/approve";

    private static string RejectUrl(long expertId, long changeId) => $"/staff/experts/{expertId}/changes/{changeId}/reject";

    private Task<HttpResponseMessage> Approve(long expertId, long changeId) =>
        _browser.Post(ApproveUrl(expertId, changeId), $"/staff/experts/{expertId}");

    private Task<HttpResponseMessage> Reject(long expertId, long changeId, string reason) =>
        _browser.Post(RejectUrl(expertId, changeId), $"/staff/experts/{expertId}", ("Reason", reason));

    /// <summary>The HTML inside &lt;section id="..."&gt;. Fails if the section is missing.</summary>
    private static string Section(string html, string id)
    {
        var match = Regex.Match(html, $"""<section[^>]*\bid="{id}"[^>]*>(.*?)</section>""", RegexOptions.Singleline);
        Assert.True(match.Success, $"No section with id=\"{id}\"");
        return match.Groups[1].Value;
    }

    private static List<string> Rows(string html) =>
        [.. Regex.Matches(html, """<tr class="change" data-id="\d+"[^>]*>.*?</tr>""", RegexOptions.Singleline).Select(match => match.Value)];

    private static List<long> Ids(string html) =>
        [.. Rows(html).Select(row => long.Parse(Regex.Match(row, @"data-id=""(\d+)""").Groups[1].Value))];

    private static string Row(string html, long id) =>
        Rows(html).Single(row => row.Contains($"data-id=\"{id}\"", StringComparison.Ordinal));

    /// <summary>The visible text: tags removed, entities decoded, spaces collapsed.</summary>
    private static string Text(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();

    /// <summary>The long label of the top-bar link to S-B, e.g. "Pending approvals (2)".</summary>
    private static string TopBarCount(string html)
    {
        var match = Regex.Match(
            html, """<a href="/staff/pending"(?: aria-current="page")?>.*?<span class="label-long">(.*?)</span>""", RegexOptions.Singleline);
        Assert.True(match.Success, "No top-bar link to /staff/pending");
        return match.Groups[1].Value;
    }

    private ChangeRow Change(long id)
    {
        using var db = _app.Open();
        return db.QuerySingle<ChangeRow>(
            "SELECT id AS Id, status AS Status, reason AS Reason, decided_by AS DecidedBy FROM ss_pending_changes WHERE id = @id",
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

    private List<string> Skills(long expertId)
    {
        using var db = _app.Open();
        return [.. db.Query<string>(
            "SELECT Skill FROM CandidateSkills WHERE CandidateId = @expertId ORDER BY Skill", new { expertId })];
    }

    private string FirstName(long expertId)
    {
        using var db = _app.Open();
        return db.ExecuteScalar<string>("SELECT FirstName FROM Candidates WHERE Id = @expertId", new { expertId })!;
    }

    // -------------------------------------------------------------------------
    // The decision cell on S-C
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Each_pending_change_has_approve_and_reject()
    {
        var row = Row(Section(await _browser.Html(AnnaPage), "pending"), AnnaSkills);

        Assert.Contains(">Approve</button>", row);
        Assert.Contains("<summary>Reject…</summary>", row);
        Assert.Contains("""<textarea id="reason-1" name="Reason" """, row);
    }

    [Fact]
    public async Task Point_1_annas_page_posts_with_annas_id()
    {
        var row = Row(Section(await _browser.Html(AnnaPage), "pending"), AnnaSkills);

        Assert.Contains($"action=\"{ApproveUrl(1, AnnaSkills)}\"", row);
        Assert.Contains($"action=\"{RejectUrl(1, AnnaSkills)}\"", row);
        Assert.DoesNotContain("action=\"/staff/pending/", row); // never S-B's URLs
    }

    [Fact]
    public async Task Point_1_bos_page_posts_with_bos_id()
    {
        var html = Section(await _browser.Html(BoPage), "pending");

        Assert.Equal([BoFirst], Ids(html));
        Assert.Contains($"action=\"{ApproveUrl(2, BoFirst)}\"", Row(html, BoFirst));
        Assert.Contains($"action=\"{RejectUrl(2, BoFirst)}\"", Row(html, BoFirst));
    }

    [Fact]
    public async Task The_log_section_still_has_no_forms_or_buttons()
    {
        AssertRedirect(await Approve(1, AnnaSkills), AnnaPage);

        var log = Section(await _browser.Html(AnnaPage), "log");

        Assert.DoesNotContain("<form", log);
        Assert.DoesNotContain("<button", log);
        Assert.DoesNotContain("<input", log);
    }

    // -------------------------------------------------------------------------
    // Approve
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Approving_returns_to_the_expert_page_with_the_message()
    {
        AssertRedirect(await Approve(1, AnnaSkills), AnnaPage);

        var html = await _browser.Html(AnnaPage);

        Assert.Matches("""<p class="decision-message[^"]*"[^>]*>""", html);
        Assert.Contains("Approved: Anna Demo – Fields of expertise", Text(html));
        Assert.Empty(Ids(Section(html, "pending")));
        Assert.Equal("approved", Change(AnnaSkills).Status);
    }

    [Fact]
    public async Task The_message_shows_once()
    {
        AssertRedirect(await Approve(1, AnnaSkills), AnnaPage);
        await _browser.Html(AnnaPage);

        Assert.DoesNotContain("decision-message", await _browser.Html(AnnaPage));
    }

    [Fact]
    public async Task An_approval_is_the_newest_log_entry_by_the_staff_user()
    {
        AssertRedirect(await Approve(1, AnnaSkills), AnnaPage);

        var log = Section(await _browser.Html(AnnaPage), "log");
        var first = Regex.Match(log, """<tr class="log-entry"[^>]*>.*?</tr>""", RegexOptions.Singleline).Value;

        Assert.Contains("Sofie Staff (secretariat)", Text(first));
        Assert.Contains("Fields of expertise", Text(first));
        Assert.Contains("EL; Maskinanlæg; Varmepumpe", Text(first));
        Assert.Contains("EL; Netværk; Varmepumpe", Text(first));
    }

    [Fact]
    public async Task Approving_skills_on_S_C_writes_the_same_as_on_S_B()
    {
        AssertRedirect(await Approve(1, AnnaSkills), AnnaPage);

        Assert.Equal(["EL", "Netværk", "Varmepumpe"], Skills(1));
        Assert.Equal(
            new LogRow(1, "staff", 1, "Skills", "EL; Maskinanlæg; Varmepumpe", "EL; Netværk; Varmepumpe", AnnaSkills),
            Assert.Single(Log()));
        Assert.Equal(1, Change(AnnaSkills).DecidedBy);
    }

    [Fact]
    public async Task Point_1_approving_bos_change_on_bos_page_changes_only_bo()
    {
        AssertRedirect(await Approve(2, BoFirst), BoPage);

        Assert.Equal("Bob", FirstName(2));
        Assert.Equal("Anna", FirstName(1));
        Assert.Equal(new LogRow(2, "staff", 1, "FirstName", "Bo", "Bob", BoFirst), Assert.Single(Log()));
        Assert.Equal("pending", Change(AnnaSkills).Status);
    }

    // -------------------------------------------------------------------------
    // Reject
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Rejecting_returns_with_the_message_stores_the_reason_and_logs_nothing()
    {
        AssertRedirect(await Reject(1, AnnaSkills, "Netværk is not your field"), AnnaPage);

        var html = await _browser.Html(AnnaPage);

        Assert.Contains("Rejected: Anna Demo – Fields of expertise", Text(html));
        Assert.Empty(Ids(Section(html, "pending")));
        Assert.Equal(new ChangeRow(AnnaSkills, "rejected", "Netværk is not your field", 1), Change(AnnaSkills));
        Assert.Empty(Log());
        Assert.Equal(["EL", "Maskinanlæg", "Varmepumpe"], Skills(1));
    }

    [Theory]
    [InlineData("", "Write a reason for rejecting")]
    [InlineData("   ", "Write a reason for rejecting")]
    [InlineData("Bad\u0007bell", "The reason can't contain control characters.")]
    public async Task A_bad_reason_shows_the_error_in_the_row_and_decides_nothing(string reason, string error)
    {
        var response = await Reject(1, AnnaSkills, reason);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        Assert.Contains("<h1>Anna Demo</h1>", html); // S-C again, not S-B
        var row = Row(Section(html, "pending"), AnnaSkills);
        Assert.Contains(error, Text(row));
        Assert.Matches("""<details class="reject"[^>]*open[^>]*>""", row);
        Assert.Equal("pending", Change(AnnaSkills).Status);
        Assert.Empty(Log());
    }

    [Fact]
    public async Task A_reason_over_500_characters_is_refused_and_kept_in_the_box()
    {
        var reason = new string('x', 501);

        var response = await Reject(1, AnnaSkills, reason);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Row(Section(await Body(response), "pending"), AnnaSkills);
        Assert.Contains("The reason can be at most 500 characters.", Text(row));
        Assert.Contains(reason, row);
        Assert.Equal("pending", Change(AnnaSkills).Status);
    }

    // -------------------------------------------------------------------------
    // The change must belong to the expert in the URL (point 1)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Point_1_approving_bos_change_under_annas_id_is_not_found()
    {
        var response = await Approve(1, BoFirst);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("pending", Change(BoFirst).Status);
        Assert.Equal("Bo", FirstName(2));
        Assert.Empty(Log());
    }

    [Fact]
    public async Task Point_1_rejecting_bos_change_under_annas_id_is_not_found()
    {
        var response = await Reject(1, BoFirst, "Wrong page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("pending", Change(BoFirst).Status);
    }

    [Fact]
    public async Task Point_1_a_bad_reason_on_bos_change_under_annas_id_is_not_found()
    {
        var response = await Reject(1, BoFirst, "");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("pending", Change(BoFirst).Status);
    }

    // -------------------------------------------------------------------------
    // Unknown, decided, access
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("/staff/experts/1/changes/999/approve")]
    [InlineData("/staff/experts/999/changes/1/approve")]
    [InlineData("/staff/experts/1/changes/999/reject")]
    [InlineData("/staff/experts/999/changes/1/reject")]
    public async Task An_unknown_change_or_expert_is_not_found(string url)
    {
        var response = await _browser.Post(url, AnnaPage, ("Reason", "No"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("pending", Change(AnnaSkills).Status);
    }

    [Fact]
    public async Task An_already_decided_change_says_so()
    {
        AssertRedirect(await Approve(1, AnnaRejected), AnnaPage);

        Assert.Contains("This change has already been decided.", Text(await _browser.Html(AnnaPage)));
        Assert.Equal("rejected", Change(AnnaRejected).Status);
        Assert.Empty(Log());
    }

    [Fact]
    public async Task An_expert_cannot_decide_even_with_a_valid_token()
    {
        var expert = new Browser(_app);
        await expert.UseEnglish();
        AssertRedirect(await expert.LogIn(Anna), "/profile");

        var response = await expert.Post(ApproveUrl(1, AnnaSkills), "/profile");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("pending", Change(AnnaSkills).Status);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task A_post_without_the_antiforgery_token_is_refused(string action)
    {
        var response = await _browser.Http.PostAsync(
            $"/staff/experts/1/changes/{AnnaSkills}/{action}", new FormUrlEncodedContent([KeyValuePair.Create("Reason", "No")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("pending", Change(AnnaSkills).Status);
    }

    [Fact]
    public async Task The_message_is_in_danish_too()
    {
        var danish = new Browser(_app);
        await danish.LogIn(Sofie);
        await danish.Get("/language/da?next=%2F");

        AssertRedirect(await danish.Post(ApproveUrl(1, AnnaSkills), AnnaPage), AnnaPage);

        Assert.Contains("Godkendt: Anna Demo – Fagområder", Text(await danish.Html(AnnaPage)));
    }

    // -------------------------------------------------------------------------
    // Point 2: the top-bar count goes down after a decision on S-C
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Point_2_the_count_goes_down_after_an_approval()
    {
        Assert.Equal("Pending approvals (2)", TopBarCount(await _browser.Html(AnnaPage)));

        AssertRedirect(await Approve(1, AnnaSkills), AnnaPage);

        Assert.Equal("Pending approvals (1)", TopBarCount(await _browser.Html(AnnaPage)));
    }

    [Fact]
    public async Task Point_2_the_count_goes_down_after_a_rejection()
    {
        AssertRedirect(await Reject(1, AnnaSkills, "No"), AnnaPage);

        Assert.Equal("Pending approvals (1)", TopBarCount(await _browser.Html(AnnaPage)));
    }

    [Fact]
    public async Task Point_2_a_bad_reason_leaves_the_count()
    {
        var response = await Reject(1, AnnaSkills, "");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Pending approvals (2)", TopBarCount(await Body(response)));
    }

    // -------------------------------------------------------------------------
    // Point 3: decided on S-C is gone from S-B, and the other way round
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Point_3_approved_on_S_C_is_gone_from_S_B()
    {
        Assert.Equal([AnnaSkills, BoFirst], Ids(await _browser.Html(PendingPage)));

        AssertRedirect(await Approve(1, AnnaSkills), AnnaPage);

        var pending = await _browser.Html(PendingPage);
        Assert.Equal([BoFirst], Ids(pending));
        Assert.Contains("1 change waiting", Text(pending));
    }

    [Fact]
    public async Task Point_3_rejected_on_S_C_is_gone_from_S_B()
    {
        AssertRedirect(await Reject(1, AnnaSkills, "No"), AnnaPage);

        Assert.Equal([BoFirst], Ids(await _browser.Html(PendingPage)));
    }

    [Fact]
    public async Task Point_3_deciding_it_again_on_S_B_says_already_decided_and_writes_nothing()
    {
        AssertRedirect(await Approve(1, AnnaSkills), AnnaPage);

        AssertRedirect(await _browser.Post($"{PendingPage}/{AnnaSkills}/reject", PendingPage, ("Reason", "Too late")), PendingPage);

        Assert.Contains("This change has already been decided.", Text(await _browser.Html(PendingPage)));
        Assert.Equal(new ChangeRow(AnnaSkills, "approved", null, 1), Change(AnnaSkills));
        Assert.Single(Log());
    }

    [Fact]
    public async Task Point_3_decided_on_S_B_is_gone_from_S_C_and_S_C_says_already_decided()
    {
        AssertRedirect(await _browser.Post($"{PendingPage}/{AnnaSkills}/approve", PendingPage), PendingPage);

        Assert.Empty(Ids(Section(await _browser.Html(AnnaPage), "pending")));

        AssertRedirect(await Reject(1, AnnaSkills, "Too late"), AnnaPage);

        Assert.Contains("This change has already been decided.", Text(await _browser.Html(AnnaPage)));
        Assert.Equal("approved", Change(AnnaSkills).Status);
        Assert.Single(Log());
    }
}
