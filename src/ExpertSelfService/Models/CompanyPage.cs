namespace ExpertSelfService.Models;

/// <summary>
/// X8 as typed. The page shows these values unchanged, so nothing typed is lost on an error.
/// The cleaned versions are what gets checked and saved.
/// </summary>
public sealed record CompanyForm(string Company, string Cvr, string Designation)
{
    public static CompanyForm From(string? company, string? cvr, string? designation) =>
        new(company ?? "", cvr ?? "", designation ?? "");
}

/// <summary>X8: the form, the expert's roles (read-only, set by VBA) and the error next to each field.</summary>
public sealed record CompanyPage(CompanyForm Form, IReadOnlyList<string> Roles, IReadOnlyDictionary<string, string> Errors)
{
    public string? Error(string field) => Errors.GetValueOrDefault(field);
}
