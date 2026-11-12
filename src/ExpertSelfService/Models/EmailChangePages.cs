namespace ExpertSelfService.Models;

/// <summary>X6a: the current email, the new email as typed, and the error next to the field.</summary>
public sealed record EmailChangePage(string CurrentEmail, string? NewEmail, string? Error);

/// <summary>X6b: where the code went, the error next to the field, and whether to link to the dev mailbox (Development only).</summary>
public sealed record EmailCodePage(string NewEmail, string? Error, bool ShowMailboxLink);
