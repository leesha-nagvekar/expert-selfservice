using ExpertSelfService.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

/// <summary>Screen X3. Only in the Development environment, and needs no login (SPEC X3).</summary>
public class DevMailboxController(DevMailbox mailbox, IWebHostEnvironment environment) : Controller
{
    [HttpGet("/dev/mailbox")]
    public IActionResult Index() => environment.IsDevelopment() ? View(mailbox.All()) : NotFound();
}
