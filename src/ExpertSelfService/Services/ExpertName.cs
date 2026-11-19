namespace ExpertSelfService.Services;

/// <summary>An expert's name as the secretariat's pages show it: First Middle Last (S-A, S-B).</summary>
public static class ExpertName
{
    public static string Full(string? firstName, string? middleName, string? lastName) =>
        string.Join(' ', new[] { firstName, middleName, lastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
