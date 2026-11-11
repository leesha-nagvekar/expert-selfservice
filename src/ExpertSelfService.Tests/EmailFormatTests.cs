using System.Text.RegularExpressions;
using ExpertSelfService.Services;

namespace ExpertSelfService.Tests;

/// <summary>
/// The email rule lives in rules.py (EMAIL_PATTERN) and in the app (EmailFormat). This test reads rules.py,
/// so a change in one language that the other doesn't follow fails here (decisions.md, "Data rules in two languages").
/// ASCII only: NOCASE and SQLite's lower() fold A-Z only (decisions.md, "Emails are compared without case").
/// </summary>
public sealed class EmailFormatTests
{
    private static Regex PatternInRulesPy()
    {
        var match = Regex.Match(RepoFiles.Read("rules.py"), """EMAIL_PATTERN = re\.compile\(r"([^"]+)"\)""");
        Assert.True(match.Success, "No EMAIL_PATTERN in rules.py");
        // Python's fullmatch: the whole text, nothing before or after.
        return new Regex($@"^(?:{match.Groups[1].Value})\z");
    }

    [Theory]
    // valid
    [InlineData("anna.demo@example.com")]
    [InlineData("ANNA.Demo@Example.COM")]
    [InlineData("first+tag@sub.example.com")]
    [InlineData("anne-marie.holm-jensen2@example.com")]
    [InlineData("a_b%c@x-y.dk")]
    // invalid, from the X1 fix
    [InlineData("bo.demo@example.com)")]
    [InlineData("bo.demo.example.com")]
    [InlineData("bo demo@example.com")]
    [InlineData("bo@@example.com")]
    [InlineData("bo.demo@")]
    [InlineData("@example.com")]
    [InlineData("bo.demo@example")]
    [InlineData("bo.demo@example..com")]
    [InlineData("not an email at all")]
    [InlineData("")]
    [InlineData("anna@ex_ample.com")]     // _ is allowed before the @, not in the domain
    // invalid: letters outside A-Z
    [InlineData("søren@example.com")]
    [InlineData("søren@kø.example.com")]
    [InlineData("bo@kø.dk")]
    [InlineData("åse@example.com")]
    [InlineData("anna@exämple.com")]
    public void EmailFormat_agrees_with_rules_py(string email)
    {
        Assert.Equal(PatternInRulesPy().IsMatch(email), EmailFormat.IsValid(email));
    }

    [Theory]
    [InlineData("søren@example.com")]
    [InlineData("søren@kø.example.com")]
    [InlineData("bo@kø.dk")]
    public void Letters_outside_a_to_z_are_not_valid(string email)
    {
        Assert.False(EmailFormat.IsValid(email));
    }
}
