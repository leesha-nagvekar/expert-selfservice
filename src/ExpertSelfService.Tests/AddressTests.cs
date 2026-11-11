using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using static ExpertSelfService.Tests.AppFactory;
using static ExpertSelfService.Tests.Browser;

namespace ExpertSelfService.Tests;

/// <summary>
/// Story E3, screen X5: the expert edits address and phone. Saved at once: Candidates is updated,
/// LastUpdated is set and one ss_change_log row per changed field is written, in one transaction (SPEC section 3).
/// </summary>
public sealed class AddressTests : IAsyncLifetime, IDisposable
{
    private const string Page = "/profile/address";
    private const string Today = "2026-11-09";          // AppFactory's clock: 9 Nov 2026, 09:00 UTC
    private const string Now = "2026-11-09T09:00:00Z";

    private const string StreetRequired = "Enter street and number.";
    private const string StreetTooLong = "Street and number can be at most 50 characters.";
    private const string PostcodeFormat = "Postcode must be 4 digits";
    private const string PostcodeUnknown = "Unknown postcode";
    private const string PhoneInvalid = "Use 8–15 digits. Spaces and a + at the start are allowed.";

    private readonly AppFactory _app = new();
    private readonly Browser _browser;

    public AddressTests() => _browser = new Browser(_app);

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

    /// <summary>Anna's current values, as the form shows them.</summary>
    private static Dictionary<string, string> AnnasForm() => new()
    {
        ["Street"] = "Østerbrogade 64",
        ["Postcode"] = "2100",
        ["PhoneMobile"] = "+45 20 11 22 33",
        ["PhoneWork"] = "33 44 55 66",
        ["PhoneHome"] = "",
    };

    private Task<HttpResponseMessage> Save(Browser browser, Dictionary<string, string> form, params (string Name, string Value)[] extra) =>
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

    private sealed record AnnaRow(string? Street, long? ZipcodeId, string? PhoneMobile, string? PhoneWork, string? PhoneHome, string? LastUpdated);

    private AnnaRow Candidate(long id = 1)
    {
        using var db = _app.Open();
        return db.QuerySingle<AnnaRow>(
            "SELECT Street, ZipcodeId, PhoneMobile, PhoneWork, PhoneHome, LastUpdated FROM Candidates WHERE Id = @id", new { id });
    }

    private sealed record LogRow(string Field, string? OldValue, string? NewValue, string ActorType, long ActorId, long? PendingChangeId, string CreatedAt);

    private List<LogRow> ChangeLog()
    {
        using var db = _app.Open();
        return db.Query<LogRow>(
            """
            SELECT field AS Field, old_value AS OldValue, new_value AS NewValue, actor_type AS ActorType,
                   actor_id AS ActorId, pending_change_id AS PendingChangeId, created_at AS CreatedAt
            FROM ss_change_log ORDER BY id
            """).ToList();
    }

    private void SetAnnasLastUpdated(string date)
    {
        using var db = _app.Open();
        db.Execute("UPDATE Candidates SET LastUpdated = @date WHERE Id = 1", new { date });
    }

    private static string Decoded(string html) => WebUtility.HtmlDecode(html);

    private static string Section(string html, string id)
    {
        var match = Regex.Match(html, $"""<section[^>]*\bid="{id}"[^>]*>(.*?)</section>""", RegexOptions.Singleline);
        Assert.True(match.Success, $"No section with id=\"{id}\"");
        return Decoded(match.Groups[1].Value);
    }

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
        Assert.Equal(new AnnaRow("Østerbrogade 64", 2001, "+45 20 11 22 33", "33 44 55 66", null, null), Candidate());
        Assert.Empty(ChangeLog());
    }

    // -------------------------------------------------------------------------
    // The form
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_profile_has_a_link_to_edit_address_and_phone()
    {
        var address = Section(await _browser.Html("/profile"), "address");

        Assert.Matches("""<a[^>]*href="/profile/address"[^>]*>\s*Edit address and phone\s*</a>""", address);
    }

    [Fact]
    public async Task The_form_is_filled_in_with_the_current_values()
    {
        var response = await _browser.Get(Page);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);

        Assert.Contains("Edit address and phone", html);
        AssertInput(html, "Street", "Østerbrogade 64");
        AssertInput(html, "Postcode", "2100");
        Assert.Contains("København Ø", Decoded(html));
        AssertInput(html, "PhoneMobile", "+45 20 11 22 33");
        AssertInput(html, "PhoneWork", "33 44 55 66");
        AssertInput(html, "PhoneHome", "");
        Assert.Contains("Save address and phone", html);
        Assert.Contains("Find city", html);
    }

    [Fact]
    public async Task An_expert_without_phones_gets_empty_phone_fields()
    {
        var bo = new Browser(_app);
        await bo.UseEnglish();
        await bo.LogIn(Bo);
        var html = await bo.Html(Page);

        AssertInput(html, "Street", "Vestergade 8");
        AssertInput(html, "Postcode", "5000");
        AssertInput(html, "PhoneMobile", "");
        AssertInput(html, "PhoneWork", "");
        AssertInput(html, "PhoneHome", "");
    }

    [Fact]
    public async Task The_city_lookup_script_is_a_file_not_inline()
    {
        var html = await _browser.Html(Page);

        Assert.Matches("""<script src="/postcode.js" defer></script>""", html);
        Assert.DoesNotMatch("""<script(?![^>]*\bsrc=)[^>]*>""", html);
        Assert.Contains("data-unknown=\"Unknown postcode\"", html);
        Assert.Equal(HttpStatusCode.OK, (await _browser.Get("/postcode.js")).StatusCode);
    }

    // -------------------------------------------------------------------------
    // Saving: Candidates, LastUpdated and the change log, together
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Saving_updates_the_address_and_last_updated_and_goes_back_to_the_profile()
    {
        var response = await Save(("Street", "Banegårdspladsen 1"), ("Postcode", "8000"));

        AssertRedirect(response, "/profile");
        var row = Candidate();
        Assert.Equal("Banegårdspladsen 1", row.Street);
        Assert.Equal(2003, row.ZipcodeId);
        Assert.Equal(Today, row.LastUpdated);
    }

    [Fact]
    public async Task The_profile_says_saved_in_the_address_section_once()
    {
        await Save(("Street", "Banegårdspladsen 1"));

        var html = await _browser.Html("/profile");
        var address = Regex.Replace(Section(html, "address"), @"\s+", " ");
        Assert.Matches("""<svg[^>]*aria-hidden="true"[^>]*>.*?</svg> Saved""", address);
        Assert.Contains("Banegårdspladsen 1", address);
        Assert.DoesNotContain("Saved", Section(html, "name"));

        Assert.DoesNotContain("Saved", Section(await _browser.Html("/profile"), "address"));
    }

    [Fact]
    public async Task The_change_log_gets_one_row_per_changed_field()
    {
        await Save(("Street", "Banegårdspladsen 1"), ("Postcode", "8000"), ("PhoneWork", ""));

        var log = ChangeLog();
        Assert.Equal(
            new List<LogRow>
            {
                new("Street", "Østerbrogade 64", "Banegårdspladsen 1", "expert", 1, null, Now),
                new("ZipcodeId", "2100 København Ø", "8000 Aarhus C", "expert", 1, null, Now),
                new("PhoneWork", "33 44 55 66", null, "expert", 1, null, Now),
            },
            log);
        Assert.Equal(3, _app.Count("SELECT COUNT(*) FROM ss_change_log WHERE candidate_id = 1"));
    }

    [Fact]
    public async Task Clearing_a_phone_stores_nothing_not_an_empty_text()
    {
        await Save(("PhoneMobile", "   "));

        Assert.Null(Candidate().PhoneMobile);
        Assert.Null(Assert.Single(ChangeLog()).NewValue);
    }

    [Fact]
    public async Task Changing_only_one_phone_writes_one_row_and_sets_last_updated()
    {
        await Save(("PhoneHome", "98 76 54 32"));

        Assert.Equal(new LogRow("PhoneHome", null, "98 76 54 32", "expert", 1, null, Now), Assert.Single(ChangeLog()));
        Assert.Equal(Today, Candidate().LastUpdated);
    }

    [Fact]
    public async Task Spaces_around_the_values_are_not_a_change()
    {
        SetAnnasLastUpdated("2025-11-14");

        var response = await Save(("Street", "  Østerbrogade 64  "), ("PhoneWork", " 33 44 55 66 "));

        AssertRedirect(response, "/profile");
        Assert.Empty(ChangeLog());
        Assert.Equal("2025-11-14", Candidate().LastUpdated);
    }

    [Fact]
    public async Task Saving_with_nothing_changed_writes_nothing_and_says_nothing()
    {
        SetAnnasLastUpdated("2025-11-14");

        AssertRedirect(await Save(), "/profile");

        Assert.Empty(ChangeLog());
        Assert.Equal("2025-11-14", Candidate().LastUpdated);
        Assert.DoesNotContain("Saved", Section(await _browser.Html("/profile"), "address"));
    }

    [Fact]
    public async Task If_the_change_log_fails_nothing_is_saved()
    {
        using (var db = _app.Open())
        {
            db.Execute(
                """
                CREATE TRIGGER test_change_log_fails BEFORE INSERT ON ss_change_log
                BEGIN SELECT RAISE(ABORT, 'test: change log is broken'); END;
                """);
        }

        var response = await Save(("Street", "Banegårdspladsen 1"), ("PhoneHome", "98 76 54 32"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var html = await Body(response);
        Assert.Contains("Something went wrong", html);
        Assert.DoesNotContain("test: change log is broken", html);
        AssertNothingWritten();
    }

    // -------------------------------------------------------------------------
    // Validation: the error next to the field, what was typed kept, nothing written
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Street", "", "street", StreetRequired)]
    [InlineData("Street", "   ", "street", StreetRequired)]
    [InlineData("Postcode", "", "postcode", PostcodeFormat)]
    [InlineData("Postcode", "123", "postcode", PostcodeFormat)]
    [InlineData("Postcode", "12a4", "postcode", PostcodeFormat)]
    [InlineData("Postcode", "21000", "postcode", PostcodeFormat)]
    [InlineData("Postcode", "9999", "postcode", PostcodeUnknown)]
    [InlineData("Postcode", "0150", "postcode", PostcodeUnknown)] // Oslo: 4 digits, in Zipcodes, but not Danish
    [InlineData("PhoneMobile", "1234567", "phone-mobile", PhoneInvalid)]          // 7 digits
    [InlineData("PhoneWork", "1234567890123456", "phone-work", PhoneInvalid)]     // 16 digits
    [InlineData("PhoneHome", "+45-20-11-22-33", "phone-home", PhoneInvalid)]
    [InlineData("PhoneMobile", "12+345678", "phone-mobile", PhoneInvalid)]
    [InlineData("PhoneMobile", "++4520112233", "phone-mobile", PhoneInvalid)]
    [InlineData("PhoneWork", "2011 2233 (work)", "phone-work", PhoneInvalid)]
    public async Task A_wrong_value_shows_the_error_next_to_its_field_and_saves_nothing(
        string field, string value, string id, string message)
    {
        var response = await Save((field, value));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        AssertFieldError(html, field, id, message);
        // What was typed stays. ASP.NET turns a value of only spaces into null, so that one comes back empty,
        // which looks the same.
        var kept = string.IsNullOrWhiteSpace(value) ? "" : value;
        AssertInput(html, field, kept);
        AssertInput(html, "Street", field == "Street" ? kept : "Østerbrogade 64"); // the rest stays as typed
        AssertNothingWritten();
    }

    [Fact]
    public async Task Several_wrong_values_each_get_their_own_error()
    {
        var html = await Body(await Save(("Street", ""), ("Postcode", "9999"), ("PhoneHome", "123")));

        AssertFieldError(html, "Street", "street", StreetRequired);
        AssertFieldError(html, "Postcode", "postcode", PostcodeUnknown);
        AssertFieldError(html, "PhoneHome", "phone-home", PhoneInvalid);
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("Street", "", "street", "Skriv vej og nummer.")]
    [InlineData("Postcode", "123", "postcode", "Postnummer skal være 4 cifre")]
    [InlineData("Postcode", "9999", "postcode", "Ukendt postnummer")]
    [InlineData("PhoneMobile", "1234567", "phone-mobile", "Brug 8–15 cifre. Mellemrum og + forrest er tilladt.")]
    public async Task The_errors_are_in_danish_too(string field, string value, string id, string message)
    {
        var danish = new Browser(_app); // Danish is the default
        await danish.LogIn(Anna);
        var form = AnnasForm();
        form[field] = value;

        AssertFieldError(await Body(await Save(danish, form)), field, id, message);
    }

    // -------------------------------------------------------------------------
    // The same rules as rules.py (decisions.md, "Data rules in two languages")
    // -------------------------------------------------------------------------

    private static int MaxLengthInRulesPy(string column)
    {
        var match = Regex.Match(RepoFiles.Read("rules.py"), $"""
            "{column}": (\d+),
            """);
        Assert.True(match.Success, $"No MAX_LENGTHS entry for {column} in rules.py");
        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Street_may_be_as_long_as_rules_py_allows_and_no_longer()
    {
        var max = MaxLengthInRulesPy("Street");

        var tooLong = await Save(("Street", new string('a', max + 1)));
        Assert.Equal(HttpStatusCode.OK, tooLong.StatusCode);
        AssertFieldError(await Body(tooLong), "Street", "street", StreetTooLong);
        AssertNothingWritten();

        AssertRedirect(await Save(("Street", new string('a', max))), "/profile");
        Assert.Equal(new string('a', max), Candidate().Street);
    }

    [Theory]
    [InlineData("PhoneMobile", "phone-mobile")]
    [InlineData("PhoneWork", "phone-work")]
    [InlineData("PhoneHome", "phone-home")]
    public async Task A_phone_may_be_as_long_as_rules_py_allows_and_no_longer(string column, string id)
    {
        var max = MaxLengthInRulesPy(column);
        // 15 digits, padded with spaces inside, so only the length is wrong.
        static string Phone(int length) => "1" + new string(' ', length - 15) + "23456789012345";

        var tooLong = await Save((column, Phone(max + 1)));
        AssertFieldError(await Body(tooLong), column, id, PhoneInvalid);
        AssertNothingWritten();

        AssertRedirect(await Save((column, Phone(max))), "/profile");
        var row = Candidate();
        var saved = column switch
        {
            "PhoneMobile" => row.PhoneMobile,
            "PhoneWork" => row.PhoneWork,
            _ => row.PhoneHome,
        };
        Assert.Equal(Phone(max), saved); // really saved, not just redirected
    }

    [Fact]
    public async Task Only_postcodes_in_the_danish_regions_of_rules_py_are_known()
    {
        var rules = RepoFiles.Read("rules.py");
        var danish = Regex.Match(rules, @"DANISH_REGION_IDS = \(([^)]*)\)").Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse).ToHashSet();
        Assert.Equal(5, danish.Count);

        // One made-up postcode in each region 1-8. Only the Danish ones may be found.
        using (var db = _app.Open())
        {
            for (var region = 1; region <= 8; region++)
            {
                db.Execute(
                    """
                    INSERT OR IGNORE INTO Regions (Id, RegionName) VALUES (@region, 'Test region');
                    INSERT INTO Cities (Id, City, RegionId, ZipCode) VALUES (9900 + @region, 'Test city', @region, '990' || @region);
                    INSERT INTO Zipcodes (Id, CityId, Zipcode) VALUES (9900 + @region, 9900 + @region, '990' || @region);
                    """,
                    new { region });
            }
        }

        for (var region = 1; region <= 8; region++)
        {
            var expected = danish.Contains(region) ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            Assert.Equal(expected, (await _browser.Get($"/postcode/990{region}")).StatusCode);
        }
    }

    [Theory]
    [InlineData("+45 20 11 22 33")] // the shapes seed.py makes
    [InlineData("20112233")]
    [InlineData("33 44 55 66")]
    [InlineData("+4520112233")]
    [InlineData("  +45 20 11 22 33  ")]
    public async Task Phones_in_the_test_data_shapes_are_valid(string phone)
    {
        AssertRedirect(await Save(("PhoneHome", phone)), "/profile");
        Assert.Equal(phone.Trim(), Candidate().PhoneHome);
    }

    // -------------------------------------------------------------------------
    // City lookup
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_city_lookup_gives_the_city_for_a_danish_postcode()
    {
        var response = await _browser.Get("/postcode/8000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await Body(response));
        Assert.Equal("Aarhus C", json.RootElement.GetProperty("city").GetString());
    }

    [Theory]
    [InlineData("9999")]  // not in Zipcodes
    [InlineData("0150")]  // Norwegian
    [InlineData("12a4")]
    [InlineData("123")]
    [InlineData("80000")]
    public async Task The_city_lookup_knows_nothing_else(string code)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _browser.Get($"/postcode/{code}")).StatusCode);
    }

    [Fact]
    public async Task The_city_lookup_is_for_logged_in_experts_only()
    {
        AssertRedirect(await new Browser(_app).Get("/postcode/8000"), "/");

        var staff = new Browser(_app);
        await staff.LogIn(Sofie);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Get("/postcode/8000")).StatusCode);
    }

    [Fact]
    public async Task Find_city_without_javascript_shows_the_city_and_saves_nothing()
    {
        var form = AnnasForm();
        form["Postcode"] = "5000";

        var response = await Save(_browser, form, ("action", "find"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await Body(response);
        AssertInput(html, "Postcode", "5000");
        Assert.Contains("Odense C", Decoded(html));
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
        var form = AnnasForm();
        form["Street"] = "Banegårdspladsen 1";

        AssertRedirect(await Save(_browser, form, (name, "2")), "/profile");

        Assert.Equal("Banegårdspladsen 1", Candidate(1).Street);
        Assert.Equal("Vestergade 8", Candidate(2).Street);
        Assert.All(ChangeLog(), row => Assert.Equal(1, row.ActorId));
        Assert.Equal(0, _app.Count("SELECT COUNT(*) FROM ss_change_log WHERE candidate_id <> 1"));
    }

    [Fact]
    public async Task An_id_in_the_path_gives_page_not_found()
    {
        var response = await _browser.Get("/profile/address/2");

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
        var post = await staff.Post(Page, "/staff", ("Street", "Hacked 1"), ("Postcode", "2100"));
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
            Page, new FormUrlEncodedContent([.. AnnasForm().Select(f => KeyValuePair.Create(f.Key, f.Value))]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNothingWritten();
    }
}
