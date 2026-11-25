// SDS web test console host.
//
// This process only serves static files. The page asks /config.js where the SDS API lives
// (setting "SdsApiUrl"), then talks to the API straight from the browser. Authentication is
// therefore the user's own Windows sign in (Negotiate), exactly as with any other API client,
// and nothing here needs to hold or forward credentials.
//
// Steps:
//   1. Read SdsApiUrl from configuration (appsettings.json, environment or command line).
//   2. Serve /config.js with that value so the browser knows the API address.
//   3. Serve wwwroot (index.html, app.js, app.css).

using System.Text.Json;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

var app = builder.Build();

// Step 1: API address, without a trailing slash so the page can append "/api/...".
var apiUrl = (app.Configuration["SdsApiUrl"] ?? "http://localhost:8350").TrimEnd('/');

// Step 2: tiny script read by index.html before app.js. JsonSerializer escapes the value, so
// a configured URL can never break out of the string literal.
app.MapGet("/config.js", () => Results.Text(
    $"window.SDS_API = {JsonSerializer.Serialize(apiUrl)};",
    "text/javascript"));

// Step 3: static application.
app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();
