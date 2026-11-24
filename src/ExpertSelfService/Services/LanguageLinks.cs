namespace ExpertSelfService.Services;

/// <summary>
/// Where the top bar's language links return to. Usually the current address, but a page shown at a POST address
/// (a bad reason on S-B or S-C) sets this ViewData key: that address has no GET, so returning there gave
/// "Page not found" (Leesha's click-through, 24 Nov).
/// </summary>
public static class LanguageLinks
{
    public const string NextKey = "LanguageNext";
}
