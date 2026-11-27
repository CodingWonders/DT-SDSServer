// SDS web test console host.
//
// This process only serves static files. The page asks /config.js where the SDS API lives
// (setting "SdsApiUrl"), then talks to the API straight from the browser. Authentication is
// therefore the user's own Windows sign in (Negotiate), exactly as with any other API client,
// and nothing here needs to hold or forward credentials.
//
// Steps:
//   0. When the SDS certificate (New-SdsCertificate.ps1) is installed, the console port speaks
//      HTTP and HTTPS (TlsOnSamePort), and plain HTTP requests are redirected to HTTPS on the
//      same address. Without it, the console is HTTP only, as before.
//   1. Create the inbound firewall rule for the console port when it is missing.
//   2. Read SdsApiUrl from configuration (appsettings.json, environment or command line).
//      When it is empty, the API is assumed to run on the same machine as the console, and
//      its address is built from the host name the browser used to open the console. With
//      SdsApiScheme "auto" (the default) that address uses HTTPS on SdsApiHttpsPort when the
//      server listens there (it does once its certificate is installed), else HTTP on
//      SdsApiPort.
//   3. Serve /config.js with that value so the browser knows the API address.
//   4. Serve wwwroot (index.html, app.js, app.css).

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Sds.Core.Networking;
using Sds.Web;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Step 0: HTTPS on the console port(s) as well, with the same certificate as the API.
var certificate = builder.Configuration.GetValue("Https", true)
    ? SdsCertificate.Find(builder.Configuration["HttpsCertificateSubject"])
    : null;
if (certificate is { } found)
{
    builder.WebHost.ConfigureKestrel(kestrel => kestrel.ConfigureEndpointDefaults(
        listen => listen.Use(TlsOnSamePort.Middleware(found.Certificate))));
}

var app = builder.Build();

if (certificate is { } active)
{
    app.Logger.LogInformation("HTTPS is on (same port as HTTP) with certificate CN={Subject} ({Thumbprint}, {Location}\\My)",
        active.Certificate.GetNameInfo(X509NameType.SimpleName, false), active.Certificate.Thumbprint, active.Location);

    // Plain HTTP goes to the same address over HTTPS. A temporary redirect, not HSTS: if the
    // certificate is removed later, http:// must work again (and HSTS would also stop browsers
    // from offering to accept a self-signed certificate).
    app.Use(async (context, next) =>
    {
        if (context.Request.IsHttps)
        {
            await next(context);
            return;
        }

        var request = context.Request;
        context.Response.Redirect($"https://{request.Host}{request.PathBase}{request.Path}{request.QueryString}");
    });
}
else
{
    app.Logger.LogInformation("HTTPS is off: no SDS certificate found (run New-SdsCertificate.ps1 next to SdsServer.exe and restart to turn it on)");
}

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
var apiScheme = (app.Configuration["SdsApiScheme"] ?? "auto").Trim().ToLowerInvariant();
var apiPort = app.Configuration.GetValue("SdsApiPort", 8350);
var apiHttpsPort = app.Configuration.GetValue("SdsApiHttpsPort", 8352);
var httpsProbe = new LoopbackPortProbe(apiHttpsPort);

// Step 3: tiny script read by index.html before app.js. JsonSerializer escapes the value, so
// neither a configured URL nor a Host header can break out of the string literal.
app.MapGet("/config.js", async (HttpContext context) =>
{
    var useHttps = apiScheme switch
    {
        "https" => true,
        "http" => false,
        _ => await httpsProbe.IsListeningAsync(),
    };

    var apiUrl = string.IsNullOrEmpty(configuredApiUrl)
        // HostString adds the brackets around IPv6 addresses.
        ? useHttps
            ? $"https://{new HostString(context.Request.Host.Host, apiHttpsPort)}"
            : $"http://{new HostString(context.Request.Host.Host, apiPort)}"
        : configuredApiUrl;

    // The answer depends on the Host header, so it must not be cached for another host name.
    context.Response.Headers.CacheControl = "no-store";
    return Results.Text($"window.SDS_API = {JsonSerializer.Serialize(apiUrl)};", "text/javascript");
});

// Step 4: static application.
app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();

/// <summary>
/// Whether something listens on a TCP port of this machine. The answer is kept for a few
/// seconds so page loads do not each open a connection.
/// </summary>
internal sealed class LoopbackPortProbe(int port)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    // A reference, so concurrent requests always read a whole answer.
    private sealed record Answer(bool Listening, DateTime Until);

    private volatile Answer? _cached;

    public async Task<bool> IsListeningAsync()
    {
        if (_cached is { } cached && DateTime.UtcNow < cached.Until)
        {
            return cached.Listening;
        }

        var listening = false;
        if (port > 0)
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            using var timeout = new CancellationTokenSource(Timeout);
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
                listening = true;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
            }
        }

        _cached = new Answer(listening, DateTime.UtcNow + CacheFor);
        return listening;
    }
}
