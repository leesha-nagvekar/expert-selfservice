namespace ExpertSelfService.Services;

/// <summary>
/// SPEC X8. The limits and the CVR weights are the C# copy of rules.py (MAX_LENGTHS, CVR_WEIGHTS);
/// CompanyTests reads rules.py, so the two can't drift apart unnoticed.
/// </summary>
public static class CompanyRules
{
    public const string Company = "Company";
    public const string Cvr = "Cvr";
    public const string Designation = "Designation";

    public const int CompanyMax = 250;
    public const int DesignationMax = 250;

    public static readonly IReadOnlyList<int> CvrWeights = [2, 7, 6, 5, 4, 3, 2, 1];

    /// <summary>The CVR as typed, without any whitespace ("12 34 56 74" -> "12345674"); empty becomes null.</summary>
    public static string? CleanCvr(string? typed) =>
        string.Concat((typed ?? "").Where(c => !char.IsWhiteSpace(c))) is { Length: > 0 } cvr ? cvr : null;

    /// <summary>Exactly 8 of the ASCII digits 0-9. Digits from other scripts don't count (decisions.md, E6).</summary>
    public static bool IsCvrFormat(string cvr) => cvr.Length == CvrWeights.Count && cvr.All(char.IsAsciiDigit);

    /// <summary>
    /// 8 digits, the first not 0, and the weighted digit sum divisible by 11. Real CVR numbers start at 10000000,
    /// and without the first-digit rule 00000000 would pass the sum (decisions.md, E6).
    /// </summary>
    public static bool IsValidCvr(string cvr) =>
        IsCvrFormat(cvr)
        && cvr[0] != '0'
        && cvr.Select((digit, i) => (digit - '0') * CvrWeights[i]).Sum() % 11 == 0;
}
