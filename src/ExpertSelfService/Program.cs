using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

// This machine only. The app is never put on the internet (CLAUDE.md, ground rule 7).
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 5080));

builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<TextService>();

// Razor would otherwise write ø as &#xF8;. Razor still escapes < > & and quotes.
builder.Services.Configure<WebEncoderOptions>(options =>
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

var app = builder.Build();

// Our own pages, in every environment. These two come first, so the developer
// exception page that ASP.NET adds in Development never sees an exception.
app.UseExceptionHandler("/error/500");
app.UseStatusCodePagesWithReExecute("/error/{0}");

// Danish unless the visitor chose otherwise (the language cookie). The browser's language is ignored.
var cultures = TextService.Languages.Select(language => new CultureInfo(language)).ToList();
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(TextService.DefaultLanguage),
    SupportedCultures = cultures,
    SupportedUICultures = cultures,
    RequestCultureProviders = [new CookieRequestCultureProvider()],
});

app.UseStaticFiles();

app.MapControllers();

app.Run();
