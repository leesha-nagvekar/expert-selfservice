namespace ExpertSelfService.Models;

/// <summary>X1: the email as typed, and the error next to the field.</summary>
public sealed record EmailPage(string? Email, string? Error);

/// <summary>X2: the error next to the field, and whether to link to the dev mailbox (Development only).</summary>
public sealed record CodePage(string? Error, bool ShowMailboxLink);
