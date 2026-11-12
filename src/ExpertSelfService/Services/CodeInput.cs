using System.Globalization;

namespace ExpertSelfService.Services;

/// <summary>A typed one-time code and the lock message, shared by X2 (login) and X6 (email change).</summary>
public static class CodeInput
{
    /// <summary>
    /// All whitespace goes, so "123 456" and pasted mail text with a non-breaking space work.
    /// Returns the 6 ASCII digits, or null: .NET's \d and char.IsDigit also accept digits from other scripts.
    /// </summary>
    public static string? Clean(string? typed)
    {
        var code = string.Concat((typed ?? "").Where(character => !char.IsWhiteSpace(character)));
        return code.Length == 6 && code.All(char.IsAsciiDigit) ? code : null;
    }

    /// <summary>"Too many attempts. Try again in N minutes.", rounded up to whole minutes.</summary>
    public static string LockedText(TextService text, TimeSpan lockedFor)
    {
        var minutes = (int)Math.Ceiling(lockedFor.TotalMinutes);
        return minutes == 1
            ? text["login.locked_one"]
            : string.Format(CultureInfo.CurrentCulture, text["login.locked"], minutes);
    }
}
