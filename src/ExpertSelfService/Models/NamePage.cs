using ExpertSelfService.Services;

namespace ExpertSelfService.Models;

/// <summary>X7 as typed. The page shows these values unchanged, so nothing typed is lost on an error.</summary>
public sealed record NameForm(string FirstName, string MiddleName, string LastName)
{
    public static NameForm From(string? firstName, string? middleName, string? lastName) =>
        new(firstName ?? "", middleName ?? "", lastName ?? "");

    public string Value(string field) => field switch
    {
        NameRules.FirstName => FirstName,
        NameRules.MiddleName => MiddleName,
        NameRules.LastName => LastName,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a name field"),
    };
}

/// <summary>
/// X7: the form, the expert's current name (shown for a locked field), the newest pending or rejected state
/// per field, and the error next to each field (keyed by column name).
/// </summary>
public sealed record NamePage(
    NameForm Form,
    NameForm Current,
    IReadOnlyDictionary<string, PendingState> States,
    IReadOnlyDictionary<string, string> Errors)
{
    public string? Error(string field) => Errors.GetValueOrDefault(field);

    public PendingState? State(string field) => States.GetValueOrDefault(field);

    /// <summary>A field with a pending change can't be edited again (SPEC section 3).</summary>
    public bool IsLocked(string field) => State(field)?.IsPending == true;

    public bool AllLocked => NameRules.Fields.All(IsLocked);
}
