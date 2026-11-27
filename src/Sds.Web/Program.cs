// SDS web test console host.
//
// This process only serves static files. The page asks /config.js where the SDS API lives
// (setting "SdsApiUrl"), then talks to the API straight from the browser. Authentication is
// therefore the user's own Windows sign in (Negotiate), exactly as with any other API client,
// and nothing here needs to hold or forward credentials.
//
// Steps:
//   1. Create the inbound firewall rule for the console port when it is missing.
//   2. Read SdsApiUrl from configuration (appsettings.json, environment or command line).
//      When it is empty, the API is assumed to run on the same machine as the console, and
//      its address is built from the host name the browser used to open the console.
//   3. Serve /config.js with that value so the browser knows the API address.
//   4. Serve wwwroot (index.html, app.js, app.css).

using System.Text.Json;
using Sds.Core.Networking;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

var app = builder.Build();

// Step 1: same behaviour as the server: create the rule once, never modify an existing one.
if (app.Configuration.GetValue("CreateFirewallRule", true))
{
    FirewallRules.EnsureForListenPorts(
        app.Configuration,
        app.Configuration["FirewallRuleName"] is { Length: > 0 } ruleName ? ruleName : "SDS Web Console (TCP-In)",
        "Allows browsers on the network to open the SDS Web Console.",
        app.Logger);
}

// Step 2: API address, without a trailing slash so the page can append "/api/...".
var configuredApiUrl = app.Configuration["SdsApiUrl"]?.Trim().TrimEnd('/');
var apiPort = app.Configuration.GetValue("SdsApiPort", 8350);

// Step 3: tiny script read by index.html before app.js. JsonSerializer escapes the value, so
// neither a configured URL nor a Host header can break out of the string literal.
app.MapGet("/config.js", (HttpContext context) =>
{
    var apiUrl = string.IsNullOrEmpty(configuredApiUrl)
        // HostString adds the brackets around IPv6 addresses.
        ? $"http://{new HostString(context.Request.Host.Host, apiPort)}"
        : configuredApiUrl;

    // The answer depends on the Host header, so it must not be cached for another host name.
    context.Response.Headers.CacheControl = "no-store";
    return Results.Text($"window.SDS_API = {JsonSerializer.Serialize(apiUrl)};", "text/javascript");
});

// Step 4: static application.
app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();
