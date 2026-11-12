using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>
/// Story E5, screen X7: the expert changes their name. It goes to approval: one ss_pending_changes row per
/// changed field, and Candidates, LastUpdated and the change log stay untouched until S2 (SPEC sections 3 and 6).
/// </summary>
public sealed class NameTests : IAsyncLifetime, IDisposable
{
    private const string Page = "/profile/name";
    private const string Now = "2026-11-09T09:00:00Z"; // AppFactory's clock

    private const string FirstRequired = "Enter your first name.";
    private const string LastRequired = "Enter your last name.";
    private const string TooLong = "The name can be at most 50 characters.";
    private const string NoLineBreaks = "The name can't contain line breaks.";

    private const string Intro = "A name change must be approved by the secretariat. Your name stays the same until it is approved.";
    private const string SendButton = "Send name for approval";

    private readonly AppFactory _app = new();
    private readonly Browser _browser;

    public NameTests() => _browser = new Browser(_app);

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

    /// <summary>Anna's current name in AppFactory, as the form shows it. She has no middle name.</summary>
    private static Dictionary<string, string> AnnasForm() => new()
    {
        ["FirstName"] = "Anna",
        ["MiddleName"] = "",
        ["LastName"] = "Demo",
    };

    private static Task<HttpResponseMessage> Send(Browser browser, Dictionary<string, string> form, params (string Name, string Value)[] extra) =>
        browser.Post(Page, Page, [.. form.Select(f => (f.Key, f.Value)), .. extra]);

    private Task<HttpResponseMessage> Send(params (string Name, string Value)[] changes)
    {
        var form = AnnasForm();
        foreach (var (name, value) in changes)
        {
            form[name] = value;
        }

        return Send(_browser, form);
    }

    private sealed record NameRow(string FirstName, string? MiddleName, string LastName, string? LastUpdated);

    private NameRow Candidate(long id = 1)
    {
        using var db = _app.Open();
        return db.QuerySingle<NameRow>(
            "SELECT FirstName, MiddleName, LastName, LastUpdated FROM Candidates WHERE Id = @id", new { id });
    }

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
    private void InsertState(string field, string? newValue, string status, string? reason = null, long candidateId = 1)
    {
        using var db = _app.Open();
        db.Execute(
            """
            INSERT INTO ss_pending_changes (candidate_id, field, old_value, new_value, status, reason, created_at, decided_by, decided_at)
            VALUES (@candidateId, @field, NULL, @newValue, @status, @reason, '2026-11-01T08:00:00Z',
                    CASE WHEN @status = 'pending' THEN NULL ELSE 1 END,
                    CASE WHEN @status = 'pending' THEN NULL ELSE '2026-11-02T08:00:00Z' END)
            """,
            new { candidateId, field, newValue, status, reason });
    }

    private void SetAnnasMiddleName(string? middleName)
    {
        using var db = _app.Open();
        db.Execute("UPDATE Candidates SET MiddleName = @middleName WHERE Id = 1", new { middleName });
    }

    private int ChangeLogRows() => _app.Count("SELECT COUNT(*) FROM ss_change_log");

    private static string Decoded(string html) => WebUtility.HtmlDecode(html);

    private static string Section(string html, string id)
    {
        var match = Regex.Match(html, $"""<section[^>]*\bid="{id}"[^>]*>(.*?)</section>""", RegexOptions.Singleline);
        Assert.True(match.Success, $"No section with id=\"{id}\"");
        return Decoded(match.Groups[1].Value);
    }

    /// <summary>The form's block for one field: from its label to the next field.</summary>
    private static string FieldBlock(string html, string id)
    {
        var match = Regex.Match(Decoded(html), $"""<div class="field[^"]*"[^>]*\bdata-field="{id}"[^>]*>(.*?)</div>\s*<!-- /field -->""", RegexOptions.Singleline);
        Assert.True(match.Success, $"No field block for {id}");
        return match.Groups[1].Value;
    }

    private static bool HasInput(string html, string name) =>
        Regex.IsMatch(html, $"""<input(?=[^>]*\bname="{name}")[^>]*>""");

    /// <summary>An input with this name and exactly this value, attributes in any order.</summary>
    private static void AssertInput(string html, string name, string value) =>
        Assert.Matches($"""<input(?=[^>]*\bname="{name}")(?=[^>]*\bvalue="{Regex.Escape(value)}")[^>]*>""", Decoded(html));

    /// <summary>The error sits next to its field: the input points at it, and it says the message.</summary>
    private static void AssertFieldError(string html, string name, string id, string message)
    {
        var decoded = Decoded(html);
        Assert.Matches($"""<input(?=[^>]*\bname="{name}")(?=[^>]*\baria-invalid="true")(?=[^>]*\baria-describedby="{id}-error")[^>]*>""", decoded);
        Assert.Matches($"""<p class="field-error" id="{id}-error">\s*{Regex.Escape(message)}\s*</p>""", decoded);
    }

    private void AssertNothingWritten()
    {
        Assert.Equal(new NameRow("Anna", null, "Demo", null), Candidate());
        Assert.Empty(Pending());
        Assert.Equal(0, ChangeLogRows());
    }

    // -------------------------------------------------------------------------
    // The form
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_profile_has_a_link_to_edit_the_name()
    {
        var name = Section(await _browser.Html("/profile"), "name");

        Assert.Matches("""<a[^>]*href="/profile/name"[^>]*>\s*Edit name\s*</a>""", name);
    }

    [Fact]
    public async Task The_form_is_filled_in_with_the_current_name()
    {
        var response = await _browser.Get(Page);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);

        Assert.Contains("Edit name", html);
        Assert.Contains(Intro, Decoded(html));
        AssertInput(html, "FirstName", "Anna");
        AssertInput(html, "MiddleName", "");
        AssertInput(html, "LastName", "Demo");
        Assert.Contains(SendButton, html);
    }

    [Fact]
    public async Task The_form_is_in_danish_too()
    {
        var danish = new Browser(_app); // Danish is the default
        await danish.LogIn(Anna);
        var html = Decoded(await danish.Html(Page));

        Assert.Contains("Rediger navn", html);
        Assert.Contains("En navneændring skal godkendes af sekretariatet. Dit navn ændres først, når den er godkendt.", html);
        Assert.Contains("Send navn til godkendelse", html);
    }

    [Fact]
    public async Task A_pending_field_has_no_input_and_shows_the_waiting_value()
    {
        InsertState("LastName", "Demo-Jensen", "pending");

        var html = await _browser.Html(Page);

        Assert.False(HasInput(html, "LastName"), "A pending field must not be editable");
        var block = FieldBlock(html, "last-name");
        Assert.Contains("Demo", block);
        Assert.Contains("Pending approval", block);
        Assert.Contains("Demo-Jensen", block);
        AssertInput(html, "FirstName", "Anna");  // the others stay editable
        AssertInput(html, "MiddleName", "");
        Assert.Contains(SendButton, html);
    }

    [Fact]
    public async Task A_rejected_field_shows_the_reason_and_can_be_edited()
    {
        InsertState("LastName", "Demo-Jensen", "rejected", "Proof of the name change is missing.");

        var html = await _browser.Html(Page);

        AssertInput(html, "LastName", "Demo");
        var block = FieldBlock(html, "last-name");
        Assert.Contains("Rejected", block);
        Assert.Contains("Demo-Jensen", block);
        Assert.Contains("Proof of the name change is missing.", block);
    }

    [Fact]
    public async Task When_all_three_fields_are_pending_there_is_no_send_button()
    {
        InsertState("FirstName", "Anne", "pending");
        InsertState("MiddleName", "Marie", "pending");
        InsertState("LastName", "Demo-Jensen", "pending");

        var html = await _browser.Html(Page);

        Assert.False(HasInput(html, "FirstName"));
        Assert.False(HasInput(html, "MiddleName"));
        Assert.False(HasInput(html, "LastName"));
        Assert.DoesNotContain(SendButton, html);
        Assert.Matches("""<a[^>]*href="/profile"[^>]*>""", html);
    }

    // -------------------------------------------------------------------------
    // Only the newest row per field decides its state (decisions.md, E2 "Which pending row counts")
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("pending")]
    [InlineData("rejected")]
    public async Task When_the_newest_row_is_approved_the_field_is_editable_again(string olderStatus)
    {
        InsertState("LastName", "Demo-Old", olderStatus, olderStatus == "rejected" ? "No proof." : null);
        InsertState("LastName", "Demo", "approved"); // newer: S2 approved it

        var html = await _browser.Html(Page);

        AssertInput(html, "LastName", "Demo");
        var block = FieldBlock(html, "last-name");
        Assert.DoesNotContain("Pending approval", block);
        Assert.DoesNotContain("Rejected", block);

        AssertRedirect(await Send(("LastName", "Demo-Hansen")), "/profile");
        var row = Pending()[^1];
        Assert.Equal(("LastName", "Demo-Hansen", "pending"), (row.Field, row.NewValue, row.Status));
    }

    // An approved row alone proves little here: the query skips approved rows anyway. The rejected row in between
    // is what makes the newest-row rule decide: without it, the field would show "Rejected" and the old reason,
    // or have two states at once.
    [Fact]
    public async Task When_the_newest_row_is_pending_older_approved_and_rejected_rows_do_not_change_it()
    {
        InsertState("LastName", "Demo", "approved");
        InsertState("LastName", "Demo-Old", "rejected", "No proof of the name change.");
        InsertState("LastName", "Demo-Jensen", "pending"); // newest

        var response = await _browser.Get(Page);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        Assert.False(HasInput(html, "LastName"));
        AssertInput(html, "FirstName", "Anna"); // the form is really there, the field is just locked
        var block = FieldBlock(html, "last-name");
        Assert.Contains("Pending approval", block);
        Assert.Contains("Demo-Jensen", block);
        Assert.DoesNotContain("Rejected", block);
        Assert.DoesNotContain("Demo-Old", block);
        Assert.DoesNotContain("No proof of the name change.", block);
    }

    // -------------------------------------------------------------------------
    // Sending: one pending row per changed field, nothing else
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Changing_the_last_name_writes_one_pending_row_and_touches_nothing_else()
    {
        var response = await Send(("LastName", "Demo-Jensen"));

        AssertRedirect(response, "/profile");
        Assert.Equal(
            new PendingRow(1, "LastName", "Demo", "Demo-Jensen", "pending", null, Now, null, null),
            Assert.Single(Pending()));
        Assert.Equal(new NameRow("Anna", null, "Demo", null), Candidate()); // not even LastUpdated
        Assert.Equal(0, ChangeLogRows());
    }

    [Fact]
    public async Task The_profile_shows_pending_approval_and_keeps_the_current_name()
    {
        await Send(("LastName", "Demo-Jensen"));

        var name = Regex.Replace(Section(await _browser.Html("/profile"), "name"), @"\s+", " ");
        Assert.Matches("""<svg[^>]*aria-hidden="true"[^>]*>.*?</svg> Pending approval""", name);
        Assert.Contains("Demo-Jensen", name);
        Assert.Contains("<dd> Demo ", name); // the current name is still there
        Assert.DoesNotContain("Saved", name);
    }

    [Fact]
    public async Task Each_row_stores_the_id_of_the_expert_in_the_session()
    {
        await Send(("FirstName", "Anne"));
        var bo = new Browser(_app);
        await bo.LogIn(Bo);
        await Send(bo, new() { ["FirstName"] = "Bent", ["MiddleName"] = "", ["LastName"] = "Demo" });

        var rows = Pending();
        Assert.Equal(2, rows.Count);
        Assert.Equal((1L, "Anne"), (rows[0].CandidateId, rows[0].NewValue));
        Assert.Equal((2L, "Bent"), (rows[1].CandidateId, rows[1].NewValue));
    }

    [Fact]
    public async Task Changing_all_three_writes_three_rows()
    {
        await Send(("FirstName", "Anne"), ("MiddleName", "Marie"), ("LastName", "Demo-Jensen"));

        Assert.Equal(
            new List<PendingRow>
            {
                new(1, "FirstName", "Anna", "Anne", "pending", null, Now, null, null),
                new(1, "MiddleName", null, "Marie", "pending", null, Now, null, null),
                new(1, "LastName", "Demo", "Demo-Jensen", "pending", null, Now, null, null),
            },
            Pending());
        Assert.Equal(new NameRow("Anna", null, "Demo", null), Candidate());
    }

    [Fact]
    public async Task Removing_the_middle_name_is_a_pending_change_to_nothing()
    {
        SetAnnasMiddleName("Marie");

        await Send(("MiddleName", "   "));

        var row = Assert.Single(Pending());
        Assert.Equal(("MiddleName", "Marie", (string?)null), (row.Field, row.OldValue, row.NewValue));
        Assert.Equal("Marie", Candidate().MiddleName);

        var name = Section(await _browser.Html("/profile"), "name");
        var pending = Regex.Match(name, """<p class="state state-pending">(.*?)</p>""", RegexOptions.Singleline);
        Assert.True(pending.Success, "No pending state in the name section");
        Assert.Contains("Not given", pending.Groups[1].Value);
    }

    [Fact]
    public async Task Sending_with_nothing_changed_writes_nothing()
    {
        AssertRedirect(await Send(), "/profile");

        AssertNothingWritten();
        Assert.DoesNotContain("Pending approval", Section(await _browser.Html("/profile"), "name"));
    }

    [Fact]
    public async Task Spaces_around_the_values_are_not_a_change()
    {
        AssertRedirect(await Send(("FirstName", "  Anna  "), ("LastName", " Demo ")), "/profile");

        AssertNothingWritten();
    }

    [Fact]
    public async Task A_change_of_case_is_a_change()
    {
        await Send(("FirstName", "anna"));

        var row = Assert.Single(Pending());
        Assert.Equal(("FirstName", "Anna", "anna"), (row.Field, row.OldValue, row.NewValue));
    }

    [Fact]
    public async Task A_rejected_field_can_be_changed_again_and_then_shows_pending()
    {
        InsertState("LastName", "Demo-Jensen", "rejected", "Proof of the name change is missing.");

        AssertRedirect(await Send(("LastName", "Demo-Hansen")), "/profile");

        var row = Pending()[^1];
        Assert.Equal(("LastName", "Demo", "Demo-Hansen", "pending"), (row.Field, row.OldValue, row.NewValue, row.Status));
        var name = Section(await _browser.Html("/profile"), "name");
        Assert.Contains("Pending approval", name);
        Assert.DoesNotContain("Rejected", name);
    }

    // -------------------------------------------------------------------------
    // A pending field is refused by the server, not just hidden by the screen
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_new_value_for_a_pending_field_is_ignored()
    {
        InsertState("LastName", "Demo-Jensen", "pending");
        var before = Assert.Single(Pending());

        AssertRedirect(await Send(("LastName", "Demo-Hansen")), "/profile");

        Assert.Equal(before, Assert.Single(Pending()));
        Assert.Equal("Demo", Candidate().LastName);
    }

    [Fact]
    public async Task A_pending_field_is_neither_checked_nor_written_while_the_others_are()
    {
        InsertState("LastName", "Demo-Jensen", "pending");

        // An empty last name would be an error, but the field is pending, so it is ignored.
        AssertRedirect(await Send(("FirstName", "Anne"), ("LastName", "")), "/profile");

        var rows = Pending();
        Assert.Equal(2, rows.Count);
        Assert.Equal(("FirstName", "Anne", "pending"), (rows[1].Field, rows[1].NewValue, rows[1].Status));
        Assert.Equal("Demo-Jensen", rows[0].NewValue);
    }

    // -------------------------------------------------------------------------
    // Validation: the error next to the field, what was typed kept, nothing written
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("FirstName", "", "first-name", FirstRequired)]
    [InlineData("FirstName", "   ", "first-name", FirstRequired)]
    [InlineData("LastName", "", "last-name", LastRequired)]
    [InlineData("LastName", "   ", "last-name", LastRequired)]
    public async Task A_missing_name_shows_the_error_next_to_its_field_and_writes_nothing(
        string field, string value, string id, string message)
    {
        var response = await Send((field, value));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        AssertFieldError(html, field, id, message);
        AssertInput(html, field, "");
        AssertInput(html, field == "FirstName" ? "LastName" : "FirstName", field == "FirstName" ? "Demo" : "Anna");
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("FirstName", "An\nna", "first-name")]
    [InlineData("FirstName", "An\r\nna", "first-name")]
    [InlineData("MiddleName", "Ma\rrie", "middle-name")]
    [InlineData("LastName", "De\tmo", "last-name")]
    [InlineData("LastName", "De\u001Fmo", "last-name")]
    [InlineData("LastName", "De\u007Fmo", "last-name")]
    [InlineData("MiddleName", "Ma\u2028rie", "middle-name")] // Unicode line separator
    [InlineData("MiddleName", "Ma\u2029rie", "middle-name")] // Unicode paragraph separator
    public async Task A_line_break_or_control_character_is_refused(string field, string value, string id)
    {
        var response = await Send((field, value));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertFieldError(await Body(response), field, id, NoLineBreaks);
        AssertNothingWritten();
    }

    /// <summary>
    /// ASP.NET itself refuses a form with a NUL character before any app code runs, so the expert gets the
    /// "Page not found" refusal (400) instead of a field error. Either way nothing is written.
    /// </summary>
    [Fact]
    public async Task A_nul_character_is_refused_before_it_reaches_the_app()
    {
        var response = await Send(("LastName", "De\u0000mo"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("FirstName", "Søren")]
    [InlineData("LastName", "Ørsted")]
    [InlineData("FirstName", "Åse")]
    [InlineData("FirstName", "José")]
    [InlineData("LastName", "O'Brien")]
    [InlineData("FirstName", "Jean-Luc")]
    [InlineData("MiddleName", "Æble")]
    [InlineData("LastName", "van der Berg")]
    [InlineData("FirstName", "Jose\u0301")] // José with a combining accent, as some keyboards type it
    public async Task Names_are_not_ascii_only(string field, string value)
    {
        AssertRedirect(await Send((field, value)), "/profile");

        var row = Assert.Single(Pending());
        Assert.Equal((field, value), (row.Field, row.NewValue));
    }

    [Fact]
    public async Task Several_wrong_values_each_get_their_own_error()
    {
        var html = await Body(await Send(("FirstName", ""), ("MiddleName", "Ma\nrie"), ("LastName", "")));

        AssertFieldError(html, "FirstName", "first-name", FirstRequired);
        AssertFieldError(html, "MiddleName", "middle-name", NoLineBreaks);
        AssertFieldError(html, "LastName", "last-name", LastRequired);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Nothing_is_sent_when_one_field_is_wrong_and_another_is_fine()
    {
        var response = await Send(("FirstName", "Anne"), ("LastName", ""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        AssertFieldError(html, "LastName", "last-name", LastRequired);
        AssertInput(html, "FirstName", "Anne"); // the valid change is kept on screen, but not sent
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("FirstName", "", "first-name", "Skriv dit fornavn.")]
    [InlineData("LastName", "", "last-name", "Skriv dit efternavn.")]
    [InlineData("LastName", "De\nmo", "last-name", "Navnet må ikke indeholde linjeskift.")]
    public async Task The_errors_are_in_danish_too(string field, string value, string id, string message)
    {
        var danish = new Browser(_app); // Danish is the default
        await danish.LogIn(Anna);
        var form = AnnasForm();
        form[field] = value;

        AssertFieldError(await Body(await Send(danish, form)), field, id, message);
    }

    [Fact]
    public async Task The_too_long_error_is_in_danish_too()
    {
        var danish = new Browser(_app);
        await danish.LogIn(Anna);
        var form = AnnasForm();
        form["FirstName"] = new string('a', MaxLengthInRulesPy("FirstName") + 1);

        AssertFieldError(await Body(await Send(danish, form)), "FirstName", "first-name", "Navnet må højst være 50 tegn.");
    }

    // -------------------------------------------------------------------------
    // The same limits as rules.py (decisions.md, "Data rules in two languages")
    // -------------------------------------------------------------------------

    private static int MaxLengthInRulesPy(string column)
    {
        var match = Regex.Match(RepoFiles.Read("rules.py"), $"""
            "{column}": (\d+),
            """);
        Assert.True(match.Success, $"No MAX_LENGTHS entry for {column} in rules.py");
        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData("FirstName", "first-name")]
    [InlineData("MiddleName", "middle-name")]
    [InlineData("LastName", "last-name")]
    public async Task A_name_may_be_as_long_as_rules_py_allows_and_no_longer(string column, string id)
    {
        var max = MaxLengthInRulesPy(column);
        Assert.Equal(50, max); // the error text says 50

        var tooLong = await Send((column, new string('a', max + 1)));
        Assert.Equal(HttpStatusCode.OK, tooLong.StatusCode);
        AssertFieldError(await Body(tooLong), column, id, TooLong);
        AssertNothingWritten();

        AssertRedirect(await Send((column, new string('a', max))), "/profile");
        Assert.Equal(new string('a', max), Assert.Single(Pending()).NewValue); // really written, not just redirected
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
        var form = AnnasForm();
        form["LastName"] = "Demo-Jensen";

        AssertRedirect(await Send(_browser, form, (name, "2")), "/profile");

        Assert.Equal(1, Assert.Single(Pending()).CandidateId);
        Assert.Equal(new NameRow("Bo", null, "Demo", null), Candidate(2));
    }

    [Fact]
    public async Task An_id_in_the_path_gives_page_not_found()
    {
        var response = await _browser.Get("/profile/name/2");

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
        var post = await staff.Post(Page, "/staff", ("FirstName", "Hacked"), ("LastName", "Demo"));
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
        var form = AnnasForm();
        form["LastName"] = "Demo-Jensen";
        var response = await _browser.Http.PostAsync(
            Page, new FormUrlEncodedContent([.. form.Select(f => KeyValuePair.Create(f.Key, f.Value))]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNothingWritten();
    }
}
