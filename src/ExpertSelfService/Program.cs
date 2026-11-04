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

// Danish unless the visitor chooses otherwise. The browser's language is ignored.
CultureInfo[] cultures = [new("da"), new("en")];
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("da"),
    SupportedCultures = cultures,
    SupportedUICultures = cultures,
    RequestCultureProviders = [],
});

app.UseStaticFiles();

app.MapControllers();

app.Run();
