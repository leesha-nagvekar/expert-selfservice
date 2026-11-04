using Microsoft.AspNetCore.Mvc;

namespace ExpertSelfService.Controllers;

public class HomeController : Controller
{
    [HttpGet("/")]
    public IActionResult Index() => View();
}
