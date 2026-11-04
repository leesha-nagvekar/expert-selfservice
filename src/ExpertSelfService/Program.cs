using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<TextService>();

// Razor would otherwise write ø as &#xF8;. Razor still escapes < > & and quotes.
builder.Services.Configure<WebEncoderOptions>(options =>
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

var app = builder.Build();

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
