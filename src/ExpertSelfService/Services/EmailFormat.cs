namespace ExpertSelfService.Services;

/// <summary>
/// A plain check that an email looks like one (SPEC X1, and later the new-email screen).
/// It catches typing mistakes, not every address the email standard allows.
/// Letters such as æ, ø and å are allowed: real domains can have them (decisions.md).
/// </summary>
public static class EmailFormat
{
    // Characters that never belong in an email typed into a login form.
    private const string Forbidden = "()<>,;:\"[]\\";

    /// <summary>True for "local@domain.tld": one @, something on both sides, no spaces or
    /// forbidden characters, and a domain with at least one dot and no empty part.</summary>
    public static bool IsValid(string email)
    {
        if (email.Any(ch => char.IsWhiteSpace(ch) || char.IsControl(ch) || Forbidden.Contains(ch)))
        {
            return false;
        }

        var parts = email.Split('@');
        if (parts.Length != 2 || parts[0].Length == 0)
        {
            return false;
        }

        var labels = parts[1].Split('.');
        return labels.Length >= 2 && labels.All(label => label.Length > 0);
    }
}
