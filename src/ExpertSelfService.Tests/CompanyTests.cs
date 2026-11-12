using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using ExpertSelfService.Services;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>
/// Story E6, screen X8: the expert edits company, CVR number and job title. Saved at once: Candidates and
/// ss_expert_company are updated, LastUpdated is set and one ss_change_log row per changed field is written,
/// in one transaction (SPEC section 3). Roles are only shown, as "Set by VBA".
/// </summary>
public sealed class CompanyTests : IAsyncLifetime, IDisposable
{
    private const string Page = "/profile/company";
    private const string Today = "2026-11-09";          // AppFactory's clock: 9 Nov 2026, 09:00 UTC
    private const string Now = "2026-11-09T09:00:00Z";

    // Anna's CVR in AppFactory, and two more that pass the modulus-11 check.
    private const string AnnasCvr = "12345674";
    private const string OtherCvr = "23456788";
    private const string ThirdCvr = "30000005";

    private const string CompanyTooLong = "The company name can be at most 250 characters.";
    private const string CompanyControl = "The company name can't contain line breaks.";
    private const string CvrFormat = "CVR number must be 8 digits.";
    private const string CvrInvalid = "This is not a valid CVR number.";
    private const string DesignationTooLong = "The job title can be at most 250 characters.";
    private const string DesignationControl = "The job title can't contain line breaks.";

    private readonly AppFactory _app = new();
    private readonly Browser _browser;

    public CompanyTests() => _browser = new Browser(_app);

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

    /// <summary>Anna's current values in AppFactory, as the form shows them.</summary>
    private static Dictionary<string, string> AnnasForm() => new()
    {
        ["Company"] = "Holm Rådgivning ApS",
        ["Cvr"] = AnnasCvr,
        ["Designation"] = "Rådgivende ingeniør",
    };

    private static Task<HttpResponseMessage> Save(Browser browser, Dictionary<string, string> form, params (string Name, string Value)[] extra) =>
        browser.Post(Page, Page, [.. form.Select(f => (f.Key, f.Value)), .. extra]);

    private Task<HttpResponseMessage> Save(params (string Name, string Value)[] changes)
    {
        var form = AnnasForm();
        foreach (var (name, value) in changes)
        {
            form[name] = value;
        }

        return Save(_browser, form);
    }

    private sealed record CompanyRow(string? Company, string? Designation, string? LastUpdated, string? Cvr);

    private CompanyRow Candidate(long id = 1)
    {
        using var db = _app.Open();
        return db.QuerySingle<CompanyRow>(
            """
            SELECT c.Company, c.Designation, c.LastUpdated, ec.cvr AS Cvr
            FROM Candidates c LEFT JOIN ss_expert_company ec ON ec.candidate_id = c.Id
            WHERE c.Id = @id
            """,
            new { id });
    }

    private int CvrRows(long id) => _app.Count("SELECT COUNT(*) FROM ss_expert_company WHERE candidate_id = @id", new { id });

    private sealed record LogRow(long CandidateId, string Field, string? OldValue, string? NewValue, string ActorType, long ActorId, long? PendingChangeId, string CreatedAt);

    private List<LogRow> ChangeLog()
    {
        using var db = _app.Open();
        return db.Query<LogRow>(
            """
            SELECT candidate_id AS CandidateId, field AS Field, old_value AS OldValue, new_value AS NewValue,
                   actor_type AS ActorType, actor_id AS ActorId, pending_change_id AS PendingChangeId, created_at AS CreatedAt
            FROM ss_change_log ORDER BY id
            """).ToList();
    }

    private static LogRow ByAnna(string field, string? oldValue, string? newValue) =>
        new(1, field, oldValue, newValue, "expert", 1, null, Now);

    private static string Decoded(string html) => WebUtility.HtmlDecode(html);

    private static string Section(string html, string id)
    {
        var match = Regex.Match(html, $"""<section[^>]*\bid="{id}"[^>]*>(.*?)</section>""", RegexOptions.Singleline);
        Assert.True(match.Success, $"No section with id=\"{id}\"");
        return Decoded(match.Groups[1].Value);
    }

    private static bool HasInput(string html, string name) =>
        Regex.IsMatch(html, $"""<(input|select|textarea)(?=[^>]*\bname="{name}")[^>]*>""", RegexOptions.IgnoreCase);

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
        Assert.Equal(new CompanyRow("Holm Rådgivning ApS", "Rådgivende ingeniør", null, AnnasCvr), Candidate());
        Assert.Empty(ChangeLog());
    }

    /// <summary>The same digits written with Arabic-Indic digits (U+0660-U+0669). Built here, so the source stays ASCII.</summary>
    private static string ArabicIndic(string digits) => string.Concat(digits.Select(digit => (char)(digit - '0' + 0x0660)));

    private static int MaxLengthInRulesPy(string column)
    {
        var match = Regex.Match(RepoFiles.Read("rules.py"), $"""
            "{column}": (\d+),
            """);
        Assert.True(match.Success, $"No MAX_LENGTHS entry for {column} in rules.py");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    // -------------------------------------------------------------------------
    // The form
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_profile_has_a_link_to_edit_company_and_job_title()
    {
        var company = Section(await _browser.Html("/profile"), "company");

        Assert.Matches("""<a[^>]*href="/profile/company"[^>]*>\s*Edit company and job title\s*</a>""", company);
    }

    [Fact]
    public async Task The_form_is_filled_in_with_the_current_values()
    {
        var response = await _browser.Get(Page);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);

        Assert.Contains("<h1>Edit company and job title</h1>", html);
        AssertInput(html, "Company", "Holm Rådgivning ApS");
        AssertInput(html, "Cvr", AnnasCvr);
        AssertInput(html, "Designation", "Rådgivende ingeniør");
        Assert.Matches("""<button[^>]*type="submit"[^>]*>\s*Save company and job title\s*</button>""", html);
        Assert.Contains("""href="/profile">Back to your profile</a>""", html);
    }

    [Fact]
    public async Task The_roles_are_shown_read_only_and_marked_set_by_vba()
    {
        var html = await _browser.Html(Page);
        var roles = Section(html, "roles");

        Assert.Contains("Set by VBA", roles);
        Assert.Contains("Roles", roles);
        Assert.Contains("Skønsmand", roles);
        Assert.False(HasInput(roles, @"[^""]*"), "The roles must not be inputs");
        Assert.False(HasInput(html, "Roles"));
    }

    [Fact]
    public async Task An_expert_with_no_company_gets_empty_fields()
    {
        var bo = new Browser(_app);
        await bo.UseEnglish();
        await bo.LogIn(Bo);

        var html = await bo.Html(Page);

        AssertInput(html, "Company", "");
        AssertInput(html, "Cvr", "");
        AssertInput(html, "Designation", "");
        Assert.Contains("Sagkyndig", Section(html, "roles"));
    }

    // -------------------------------------------------------------------------
    // Saving: Candidates, ss_expert_company, LastUpdated and the change log, together
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Changing_all_three_saves_them_at_once_and_logs_each_field()
    {
        var response = await Save(("Company", "Berg & Co. A/S"), ("Cvr", OtherCvr), ("Designation", "Byggeleder"));

        AssertRedirect(response, "/profile");
        Assert.Equal(new CompanyRow("Berg & Co. A/S", "Byggeleder", Today, OtherCvr), Candidate());
        Assert.Equal(
            [
                ByAnna("Company", "Holm Rådgivning ApS", "Berg & Co. A/S"),
                ByAnna("Cvr", AnnasCvr, OtherCvr),
                ByAnna("Designation", "Rådgivende ingeniør", "Byggeleder"),
            ],
            ChangeLog());
    }

    [Fact]
    public async Task The_profile_says_saved_in_the_company_section_once()
    {
        await Save(("Designation", "Byggeleder"));

        var html = await _browser.Html("/profile");
        var company = Regex.Replace(Section(html, "company"), @"\s+", " ");
        Assert.Matches("""<svg[^>]*aria-hidden="true"[^>]*>.*?</svg> Saved""", company);
        Assert.Contains("Byggeleder", company);
        Assert.DoesNotContain("Saved", Section(html, "address"));

        Assert.DoesNotContain("Saved", Section(await _browser.Html("/profile"), "company"));
    }

    [Fact]
    public async Task Changing_one_field_writes_one_log_row()
    {
        await Save(("Designation", "Byggeleder"));

        Assert.Equal([ByAnna("Designation", "Rådgivende ingeniør", "Byggeleder")], ChangeLog());
        Assert.Equal(new CompanyRow("Holm Rådgivning ApS", "Byggeleder", Today, AnnasCvr), Candidate());
    }

    [Fact]
    public async Task Saving_without_a_change_writes_nothing_and_says_nothing()
    {
        // Spaces around a value, and spaces inside the CVR, are not a change.
        var response = await Save(("Company", "  Holm Rådgivning ApS "), ("Cvr", " 1234 5674 "), ("Designation", "Rådgivende ingeniør "));

        AssertRedirect(response, "/profile");
        AssertNothingWritten();
        Assert.DoesNotContain("Saved", Section(await _browser.Html("/profile"), "company"));
    }

    [Fact]
    public async Task A_change_of_case_is_a_change()
    {
        await Save(("Company", "HOLM Rådgivning ApS"));

        Assert.Equal([ByAnna("Company", "Holm Rådgivning ApS", "HOLM Rådgivning ApS")], ChangeLog());
    }

    [Fact]
    public async Task Clearing_the_cvr_deletes_its_row_and_logs_it()
    {
        AssertRedirect(await Save(("Cvr", "")), "/profile");

        Assert.Equal(0, CvrRows(1));
        Assert.Equal([ByAnna("Cvr", AnnasCvr, null)], ChangeLog());
        Assert.Contains("Not given", Section(await _browser.Html("/profile"), "company"));
    }

    [Fact]
    public async Task Clearing_company_and_job_title_stores_nothing_rather_than_empty_text()
    {
        await Save(("Company", " "), ("Designation", ""));

        Assert.Equal(new CompanyRow(null, null, Today, AnnasCvr), Candidate());
        Assert.Equal(
            [
                ByAnna("Company", "Holm Rådgivning ApS", null),
                ByAnna("Designation", "Rådgivende ingeniør", null),
            ],
            ChangeLog());
    }

    [Theory]
    [InlineData("23 45 67 88")]
    [InlineData(" 2345 6788 ")]
    [InlineData("2345\t6788")]
    public async Task Spaces_in_a_cvr_are_removed(string typed)
    {
        AssertRedirect(await Save(("Cvr", typed)), "/profile");

        Assert.Equal(OtherCvr, Candidate().Cvr);
        Assert.Equal([ByAnna("Cvr", AnnasCvr, OtherCvr)], ChangeLog());
    }

    [Fact]
    public async Task An_expert_without_a_cvr_gets_one_and_may_share_it_with_a_colleague()
    {
        // Bo has no company and no CVR row. He saves Anna's CVR: two experts at one company (decisions.md, E6).
        var bo = new Browser(_app);
        await bo.UseEnglish();
        await bo.LogIn(Bo);

        var response = await bo.Post(Page, Page, ("Company", "Holm Rådgivning ApS"), ("Cvr", AnnasCvr), ("Designation", ""));

        AssertRedirect(response, "/profile");
        Assert.Equal(new CompanyRow("Holm Rådgivning ApS", null, Today, AnnasCvr), Candidate(2));
        Assert.Equal(1, CvrRows(2));
        Assert.Equal(AnnasCvr, Candidate(1).Cvr);
        Assert.Equal(
            [
                new LogRow(2, "Company", null, "Holm Rådgivning ApS", "expert", 2, null, Now),
                new LogRow(2, "Cvr", null, AnnasCvr, "expert", 2, null, Now),
            ],
            ChangeLog());
    }

    [Fact]
    public async Task Saving_never_touches_another_expert()
    {
        await Save(("Company", "Berg & Co. A/S"), ("Cvr", ThirdCvr), ("Designation", "Byggeleder"));

        Assert.Equal(new CompanyRow(null, null, null, null), Candidate(2));
        Assert.Equal(0, CvrRows(2));
        Assert.All(ChangeLog(), row => Assert.Equal(1, row.CandidateId));
    }

    [Theory]
    [InlineData("Company", "Holm & Co. ApS")]
    [InlineData("Company", "Ørsted A/S")]
    [InlineData("Company", "O'Neill Byg I/S")]
    [InlineData("Company", "Åkande Æblegård")]
    [InlineData("Designation", "Ingeniør, cand.polyt.")]
    [InlineData("Designation", "Tømrermester (svend 1998)")]
    public async Task Ordinary_company_names_and_job_titles_are_accepted(string field, string value)
    {
        AssertRedirect(await Save((field, value)), "/profile");

        var row = Candidate();
        Assert.Equal(value, field == "Company" ? row.Company : row.Designation);
    }

    // -------------------------------------------------------------------------
    // Validation: the error next to the field, the value kept, nothing written
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("abcdefgh")]
    [InlineData("1234-5674")]
    [InlineData("12345674x")]
    [InlineData("+12345674")]
    public async Task A_cvr_that_is_not_8_digits_is_refused(string cvr)
    {
        var response = await Save(("Cvr", cvr));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        AssertFieldError(html, "Cvr", "cvr", CvrFormat);
        AssertInput(html, "Cvr", cvr);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Digits_from_another_script_are_not_digits_here()
    {
        // Anna's valid CVR in Arabic-Indic digits. Only 0-9 count (decisions.md, E6).
        var response = await Save(("Cvr", ArabicIndic(AnnasCvr)));

        AssertFieldError(await Body(response), "Cvr", "cvr", CvrFormat);
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("12345675")] // the check digit is wrong
    [InlineData("97663441")]
    [InlineData("00000000")] // the weighted sum is 0, divisible by 11, but a CVR never starts with 0
    [InlineData("00000019")] // 1*2 + 9*1 = 11
    public async Task A_cvr_that_fails_the_check_is_refused(string cvr)
    {
        var response = await Save(("Cvr", cvr));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        AssertFieldError(html, "Cvr", "cvr", CvrInvalid);
        AssertInput(html, "Cvr", cvr);
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("Company", "company", "\n", CompanyControl)]
    [InlineData("Company", "company", "\r", CompanyControl)]
    [InlineData("Company", "company", "\t", CompanyControl)]
    [InlineData("Company", "company", "LS", CompanyControl)]
    [InlineData("Designation", "designation", "\n", DesignationControl)]
    [InlineData("Designation", "designation", "PS", DesignationControl)]
    public async Task A_line_break_or_control_character_is_refused(string field, string id, string character, string message)
    {
        // LS and PS stand for U+2028 and U+2029, built here so the source has no raw separators.
        var inside = character switch
        {
            "LS" => ((char)0x2028).ToString(),
            "PS" => ((char)0x2029).ToString(),
            _ => character,
        };

        var response = await Save((field, $"Holm{inside}Byg"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertFieldError(await Body(response), field, id, message);
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("Company", "company", CompanyTooLong)]
    [InlineData("Designation", "designation", DesignationTooLong)]
    public async Task A_text_may_be_as_long_as_rules_py_allows_and_no_longer(string column, string id, string message)
    {
        var max = MaxLengthInRulesPy(column);
        Assert.Equal(250, max); // the error text says 250

        var tooLong = await Save((column, new string('a', max + 1)));
        Assert.Equal(HttpStatusCode.OK, tooLong.StatusCode);
        AssertFieldError(await Body(tooLong), column, id, message);
        AssertNothingWritten();

        AssertRedirect(await Save((column, new string('a', max))), "/profile");
        var row = Candidate();
        Assert.Equal(new string('a', max), column == "Company" ? row.Company : row.Designation);
    }

    [Fact]
    public async Task All_errors_are_shown_at_once_and_every_value_is_kept()
    {
        var company = new string('a', 251);
        var response = await Save(("Company", company), ("Cvr", "1234"), ("Designation", "Byg\nleder"));

        var html = await Body(response);
        AssertFieldError(html, "Company", "company", CompanyTooLong);
        AssertFieldError(html, "Cvr", "cvr", CvrFormat);
        AssertFieldError(html, "Designation", "designation", DesignationControl);
        AssertInput(html, "Company", company);
        AssertInput(html, "Cvr", "1234");
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("Cvr", "1234", "cvr", "CVR-nummeret skal være 8 cifre.")]
    [InlineData("Cvr", "12345675", "cvr", "Det er ikke et gyldigt CVR-nummer.")]
    [InlineData("Company", "Holm\nByg", "company", "Firmanavnet må ikke indeholde linjeskift.")]
    [InlineData("Designation", "Byg\nleder", "designation", "Stillingsbetegnelsen må ikke indeholde linjeskift.")]
    public async Task The_errors_are_in_danish_too(string field, string value, string id, string message)
    {
        var danish = new Browser(_app); // Danish is the default
        await danish.LogIn(Anna);
        var form = AnnasForm();
        form[field] = value;

        AssertFieldError(await Body(await Save(danish, form)), field, id, message);
    }

    [Theory]
    [InlineData("Company", "company", "Firmanavnet må højst være 250 tegn.")]
    [InlineData("Designation", "designation", "Stillingsbetegnelsen må højst være 250 tegn.")]
    public async Task The_too_long_errors_are_in_danish_too(string field, string id, string message)
    {
        var danish = new Browser(_app);
        await danish.LogIn(Anna);
        var form = AnnasForm();
        form[field] = new string('a', 251);

        AssertFieldError(await Body(await Save(danish, form)), field, id, message);
    }

    [Fact]
    public async Task The_page_is_in_danish_too()
    {
        var danish = new Browser(_app);
        await danish.LogIn(Anna);

        var html = Decoded(await danish.Html(Page));
        Assert.Contains("<h1>Rediger firma og stillingsbetegnelse</h1>", html);
        Assert.Matches("""<button[^>]*type="submit"[^>]*>\s*Gem firma og stillingsbetegnelse\s*</button>""", html);
        Assert.Contains("Fastsat af VBA", Section(html, "roles"));
        Assert.Matches("""<a[^>]*href="/profile/company"[^>]*>\s*Rediger firma og stillingsbetegnelse\s*</a>""", Decoded(await danish.Html("/profile")));
    }

    // -------------------------------------------------------------------------
    // The same CVR rule as rules.py (decisions.md, "Data rules in two languages")
    // -------------------------------------------------------------------------

    [Fact]
    public void The_cvr_weights_are_the_same_as_in_rules_py()
    {
        var match = Regex.Match(RepoFiles.Read("rules.py"), @"CVR_WEIGHTS = \(([^)]*)\)");
        Assert.True(match.Success, "No CVR_WEIGHTS in rules.py");
        var weights = match.Groups[1].Value.Split(',').Select(w => int.Parse(w.Trim(), CultureInfo.InvariantCulture));

        Assert.Equal(weights, CompanyRules.CvrWeights);
    }

    [Theory]
    [InlineData("97663440")] // the same valid cases as tests/test_check_data.py
    [InlineData("10297877")]
    [InlineData("10000009")]
    [InlineData(AnnasCvr)]
    [InlineData(OtherCvr)]
    [InlineData(ThirdCvr)]
    public void Valid_cvr_numbers_pass(string cvr) => Assert.True(CompanyRules.IsValidCvr(cvr));

    [Theory]
    [InlineData("97663441")] // the same invalid cases as tests/test_check_data.py
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("abcdefgh")]
    [InlineData("")]
    [InlineData("00000000")]
    [InlineData("00000019")]
    public void Invalid_cvr_numbers_fail(string cvr) => Assert.False(CompanyRules.IsValidCvr(cvr));

    [Fact]
    public void Arabic_indic_digits_fail() => Assert.False(CompanyRules.IsValidCvr(ArabicIndic("97663440")));

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
        form["Designation"] = "Byggeleder";

        AssertRedirect(await Save(_browser, form, (name, "2")), "/profile");

        Assert.Equal("Byggeleder", Candidate(1).Designation);
        Assert.Equal(new CompanyRow(null, null, null, null), Candidate(2));
    }

    [Fact]
    public async Task Roles_and_status_in_the_form_are_ignored()
    {
        var form = AnnasForm();
        form["Designation"] = "Byggeleder";

        AssertRedirect(await Save(_browser, form, ("Roles", "Mediator"), ("TagId", "36"), ("Status", "0")), "/profile");

        Assert.Equal(1, _app.Count("SELECT Status FROM Candidates WHERE Id = 1"));
        Assert.Equal(35, _app.Count("SELECT TagId FROM CandidateTags WHERE CandidateId = 1"));
        Assert.Equal(1, _app.Count("SELECT COUNT(*) FROM CandidateTags WHERE CandidateId = 1"));
    }

    [Fact]
    public async Task An_id_in_the_path_gives_page_not_found()
    {
        var response = await _browser.Get("/profile/company/2");

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
        var post = await staff.Post(Page, "/staff", ("Company", "Hacked"), ("Cvr", ""), ("Designation", ""));
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
        form["Designation"] = "Byggeleder";
        var response = await _browser.Http.PostAsync(
            Page, new FormUrlEncodedContent([.. form.Select(f => KeyValuePair.Create(f.Key, f.Value))]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNothingWritten();
    }
}
