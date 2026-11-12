using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using ExpertSelfService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.WebEncoders;

// .env sits in the repo root, next to global.json. Variables already set in the environment win.
var root = AppSettings.FindProjectRoot(AppContext.BaseDirectory);
AppSettings.LoadEnvFile(Path.Combine(root, ".env"));

var builder = WebApplication.CreateBuilder(args);

// This machine only. The app is never put on the internet (CLAUDE.md, ground rule 7).
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 5080));

// CSRF: every POST must carry the antiforgery token, for all controllers, not one by one.
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddSingleton<TextService>();
builder.Services.AddSingleton(services => AppSettings.Load(services.GetRequiredService<IConfiguration>(), root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<DevMailbox>();
builder.Services.AddSingleton<OneTimeCodes>();
builder.Services.AddSingleton<LoginService>();
builder.Services.AddSingleton<ProfileService>();
builder.Services.AddSingleton<ExpertSessionCheck>();
builder.Services.AddSingleton<ExpertChanges>();
builder.Services.AddSingleton<AddressService>();
builder.Services.AddSingleton<EmailChangeService>();
builder.Services.AddSingleton<NameService>();

// Login cookie. ASP.NET encrypts it with Data Protection keys kept in the Windows user profile,
// outside the repo. It holds only expert_id or staff_id (SPEC section 2).
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;
        // Not logged in: the front page (X1), without a ReturnUrl, so no link can redirect off the site.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.Redirect("/");
            return Task.CompletedTask;
        };
        // Wrong kind of user: a 403, which the error page shows as "Page not found".
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        // Every request: an expert set to Retired or removed since logging in is logged out on any page.
        // The request then goes on as "not logged in", so expert pages send them to "/".
        options.Events.OnValidatePrincipal = async context =>
        {
            var check = context.HttpContext.RequestServices.GetRequiredService<ExpertSessionCheck>();
            if (context.Principal is { } user && !check.IsStillValid(user))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
// The cookie's expiry uses the app's clock, so tests can move time forward.
builder.Services
    .AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(SessionClaims.ExpertPolicy, policy => policy.RequireClaim(SessionClaims.ExpertId))
    .AddPolicy(SessionClaims.StaffPolicy, policy => policy.RequireClaim(SessionClaims.StaffId));

// Razor would otherwise write ø as &#xF8;. Razor still escapes < > & and quotes.
builder.Services.Configure<WebEncoderOptions>(options =>
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

var app = builder.Build();

// Fail at start, with a clear message, rather than in the middle of a demo.
app.Services.GetRequiredService<AppSettings>();

// Our own pages, in every environment. These two come first, so the developer
// exception page that ASP.NET adds in Development never sees an exception.
app.UseExceptionHandler("/error/500");
app.UseStatusCodePagesWithReExecute("/error/{0}");

// Danish unless the visitor chose otherwise (the language cookie). The browser's language is ignored.
// Texts follow the language (da, en). Dates and numbers follow its format culture (da-DK, en-GB).
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(
        TextService.FormatCulture(TextService.DefaultLanguage), new CultureInfo(TextService.DefaultLanguage)),
    SupportedCultures = [.. TextService.Languages.Select(TextService.FormatCulture)],
    SupportedUICultures = [.. TextService.Languages.Select(language => new CultureInfo(language))],
    RequestCultureProviders = [new CookieRequestCultureProvider()],
});

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
