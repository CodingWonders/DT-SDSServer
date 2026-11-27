// SDS server entry point.
//
// The process hosts the REST API (Kestrel), the background job runner and the maintenance
// loop. It runs as a console application for development or as the Windows service "SDS":
//   sc.exe create SDS binPath= "C:\Program Files\SDS\SdsServer.exe" start= auto
//
// Startup order:
//   1. Serilog (console, rolling file in %ProgramData%\SDS\Logs, Windows Event Log).
//   2. Core services and hosted services (share bootstrap, migration, reconcile, jobs).
//   3. Windows Negotiate authentication and the SdsAdmin / SdsReader policies.
//   4. Controllers, ProblemDetails error mapping and the OpenAPI document.

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Negotiate;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Sds.Core;
using Sds.Server.Infrastructure;
using Sds.Server.Security;
using Serilog;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,

    // A Windows service starts in System32; use the install folder for appsettings.json.
    ContentRootPath = AppContext.BaseDirectory,
});

// ---------------------------------------------------------------------
// Section 1: hosting and logging
// ---------------------------------------------------------------------
builder.Host.UseWindowsService(o => o.ServiceName = "SDS");

var dataDirectory = builder.Configuration.GetSection(SdsOptions.Section).Get<SdsOptions>()?.ResolvedDataDirectory
    ?? new SdsOptions().ResolvedDataDirectory;
builder.Services.AddSerilog((services, logger) =>
{
    logger.ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(Path.Combine(dataDirectory, "Logs", "sds-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30);

    // The event log source can only be created by an administrator; skip it otherwise.
    if (builder.Configuration.GetValue("Sds:EventLog", true) && OperatingSystem.IsWindows())
    {
        logger.WriteTo.EventLog("SDS", manageEventSource: true, restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning);
    }
});

// ---------------------------------------------------------------------
// Section 2: SDS core
// ---------------------------------------------------------------------
builder.Services.AddSdsCore(builder.Configuration);
builder.Services.AddSingleton<UploadStore>();

// ---------------------------------------------------------------------
// Section 3: security
// ---------------------------------------------------------------------
// Two ways in: Windows Negotiate, and password sessions (POST /api/auth/login checks a Windows
// user name and password and issues a cookie). The default scheme forwards each request to
// one of them: the cookie when present and no Authorization header is sent, else Negotiate.
// Negotiate inspects every request and needs Kestrel connection features, so the in memory
// test server registers its own "Test" scheme instead (Sds:TestAuthentication, never set in
// production).
var testAuthentication = builder.Configuration.GetValue<bool>("Sds:TestAuthentication");
var windowsScheme = testAuthentication ? "Test" : NegotiateDefaults.AuthenticationScheme;
var sessionMinutes = builder.Configuration.GetValue("Sds:SessionMinutes", new SdsOptions().SessionMinutes);
var authentication = builder.Services.AddAuthentication(SdsAuth.Scheme)
    .AddPolicyScheme(SdsAuth.Scheme, "Windows or password session", o => o.ForwardDefaultSelector = ctx => SdsAuth.Select(ctx, windowsScheme))
    .AddCookie(SdsAuth.PasswordScheme, o => SdsAuth.ConfigureCookie(o, sessionMinutes));
if (!testAuthentication)
{
    authentication.AddNegotiate();
}
builder.Services.AddSingleton<ICredentialValidator, WindowsCredentialValidator>();

// Password guessing brake: at most 10 sign-in attempts per client address every 5 minutes
// (Windows account lockout policy applies on top).
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy(SdsAuth.LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many sign-in attempts",
            Detail = "Too many sign-in attempts from this address. Wait a few minutes and try again.",
        }, ct);
    };
});
builder.Services.AddSingleton<IAuthorizationHandler, SdsRoleHandler>();
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(SdsPolicies.Admin, p => p.RequireAuthenticatedUser().AddRequirements(new SdsRoleRequirement(AdminOnly: true)));
    o.AddPolicy(SdsPolicies.Reader, p => p.RequireAuthenticatedUser().AddRequirements(new SdsRoleRequirement(AdminOnly: false)));

    // Anything not explicitly marked otherwise needs at least an authenticated caller.
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// ---------------------------------------------------------------------
// Section 4: API surface
// ---------------------------------------------------------------------
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SdsExceptionHandler>();
builder.Services.AddOpenApi();

// Browser front ends hosted elsewhere (for example the Sds.Web test console) call the API
// cross origin with Windows credentials. Allowed: the origins listed in Sds:CorsOrigins, and
// the console on this machine (port Sds:ConsolePort) under whatever host name the browser
// used. Credentials require explicit origins (no wildcard). See SdsCorsPolicyProvider.
builder.Services.AddSingleton<ICorsPolicyProvider, SdsCorsPolicyProvider>();
builder.Services.AddCors();

// WIM files are large: lift the request size limit (individual endpoints may still restrict).
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = null);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// CORS runs before authentication so that preflight (OPTIONS) requests, which browsers send
// without credentials, are answered instead of being challenged with 401 Negotiate.
app.UseCors();

// Over HTTPS the session cookie becomes SameSite=None; Secure, so a console served from another
// host (another site) still gets it on its cross origin calls. Over HTTP it stays Lax.
app.UseCookiePolicy(new CookiePolicyOptions
{
    OnAppendCookie = c => SecureOverHttps(c.Context, c.CookieOptions),
    OnDeleteCookie = c => SecureOverHttps(c.Context, c.CookieOptions),
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapOpenApi().AllowAnonymous();

app.Run();

static void SecureOverHttps(HttpContext context, CookieOptions cookie)
{
    if (context.Request.IsHttps)
    {
        cookie.SameSite = SameSiteMode.None;
        cookie.Secure = true;
    }
}

/// <summary>Entry point type, exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program;
