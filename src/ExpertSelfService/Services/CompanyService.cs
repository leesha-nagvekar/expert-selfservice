using Dapper;
using ExpertSelfService.Models;
using Microsoft.Data.Sqlite;

namespace ExpertSelfService.Services;

/// <summary>
/// Story E6, screen X8: company, CVR number and job title, saved at once (SPEC section 3). The CVR lives in
/// ss_expert_company, because VBA's Candidates table has no column for it (decisions.md, 2 Nov 2026).
/// </summary>
public sealed class CompanyService(Database database, ExpertChanges changes)
{
    private sealed record CurrentRow(string? Company, string? Designation, string? Cvr);

    /// <summary>The form with the current values and the roles, or null when the expert is gone or Retired.</summary>
    public CompanyPage? Load(long expertId)
    {
        using var db = database.Open();
        // Deferred: a read transaction, so the values and the roles are from the same moment.
        using var transaction = db.BeginTransaction(deferred: true);
        var current = Current(db, transaction, expertId);
        if (current is null)
        {
            return null;
        }

        var roles = ProfileService.Roles(db, transaction, expertId);
        transaction.Commit();

        return new CompanyPage(
            CompanyForm.From(current.Company, current.Cvr, current.Designation), roles, new Dictionary<string, string>());
    }

    /// <summary>The roles alone, for showing the form again after an error.</summary>
    public IReadOnlyList<string> Roles(long expertId)
    {
        using var db = database.Open();
        return ProfileService.Roles(db, null, expertId);
    }

    /// <summary>The i18n key of the error for each wrong field. All three are optional; every field is checked (as E3).</summary>
    public static Dictionary<string, string> Validate(CompanyForm form)
    {
        var errors = new Dictionary<string, string>();

        CheckText(errors, CompanyRules.Company, form.Company, CompanyRules.CompanyMax, "company.error.company_control", "company.error.company_too_long");
        CheckText(errors, CompanyRules.Designation, form.Designation, CompanyRules.DesignationMax, "company.error.designation_control", "company.error.designation_too_long");

        if (CompanyRules.CleanCvr(form.Cvr) is string cvr)
        {
            if (!CompanyRules.IsCvrFormat(cvr))
            {
                errors[CompanyRules.Cvr] = "company.error.cvr_format";
            }
            else if (!CompanyRules.IsValidCvr(cvr))
            {
                errors[CompanyRules.Cvr] = "company.error.cvr_invalid";
            }
        }

        return errors;
    }

    /// <summary>
    /// Saves a valid form. Candidates, ss_expert_company, LastUpdated and one change-log row per changed field
    /// are written in one transaction; nothing is written when nothing changed.
    /// </summary>
    public SaveResult Save(long expertId, CompanyForm form)
    {
        using var db = database.Open();
        // Not deferred: the write lock is taken at the start, so the values compared are the ones overwritten.
        using var transaction = db.BeginTransaction();

        var current = Current(db, transaction, expertId);
        if (current is null)
        {
            return SaveResult.ExpertNotFound;
        }

        var company = TextRules.Clean(form.Company);
        var cvr = CompanyRules.CleanCvr(form.Cvr);
        var designation = TextRules.Clean(form.Designation);

        // In the order of the form. Exact comparison: a change of case is a change; empty and missing are the same.
        var changed = new List<FieldChange>();
        AddIfChanged(changed, CompanyRules.Company, current.Company, company);
        AddIfChanged(changed, CompanyRules.Cvr, current.Cvr, cvr);
        AddIfChanged(changed, CompanyRules.Designation, current.Designation, designation);
        if (changed.Count == 0)
        {
            return SaveResult.NothingChanged;
        }

        db.Execute(
            "UPDATE Candidates SET Company = @company, Designation = @designation WHERE Id = @expertId",
            new { company, designation, expertId },
            transaction);

        // A row exists only when there is a CVR, as the seed makes them.
        if (cvr is null)
        {
            db.Execute("DELETE FROM ss_expert_company WHERE candidate_id = @expertId", new { expertId }, transaction);
        }
        else
        {
            db.Execute(
                """
                INSERT INTO ss_expert_company (candidate_id, cvr) VALUES (@expertId, @cvr)
                ON CONFLICT (candidate_id) DO UPDATE SET cvr = excluded.cvr
                """,
                new { expertId, cvr },
                transaction);
        }

        changes.Record(db, transaction, expertId, ExpertChanges.ByExpert, expertId, changed);

        transaction.Commit();
        return SaveResult.Saved;
    }

    private static void CheckText(Dictionary<string, string> errors, string field, string value, int max, string controlKey, string tooLongKey)
    {
        var trimmed = value.Trim();
        if (TextRules.HasControlCharacter(trimmed))
        {
            errors[field] = controlKey;
        }
        else if (trimmed.Length > max)
        {
            errors[field] = tooLongKey;
        }
    }

    private static void AddIfChanged(List<FieldChange> changed, string field, string? oldValue, string? newValue)
    {
        var old = string.IsNullOrEmpty(oldValue) ? null : oldValue;
        if (!string.Equals(old, newValue, StringComparison.Ordinal))
        {
            changed.Add(new FieldChange(field, old, newValue));
        }
    }

    private static CurrentRow? Current(SqliteConnection db, SqliteTransaction transaction, long expertId) =>
        db.QuerySingleOrDefault<CurrentRow>(
            """
            SELECT c.Company, c.Designation, ec.cvr AS Cvr
            FROM Candidates c
            LEFT JOIN ss_expert_company ec ON ec.candidate_id = c.Id
            WHERE c.Id = @expertId AND c.Status <> @retired
            """,
            new { expertId, retired = ExpertStatus.Retired },
            transaction);
}
