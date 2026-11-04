using ExpertSelfService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<TextService>();

var app = builder.Build();

app.UseStaticFiles();

app.Run();
