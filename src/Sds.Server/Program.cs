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
using Microsoft.AspNetCore.Authorization;
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
// Negotiate inspects every request and needs Kestrel connection features, so the in memory
// test server replaces it with its own scheme (Sds:TestAuthentication, never set in production).
if (!builder.Configuration.GetValue<bool>("Sds:TestAuthentication"))
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
}
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
// cross origin with Windows credentials. Only the origins listed in Sds:CorsOrigins are
// allowed, and credentials require explicit origins (no wildcard).
var corsOrigins = builder.Configuration.GetSection("Sds:CorsOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(corsOrigins)
    .AllowCredentials()
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Location", "Content-Disposition")));

// WIM files are large: lift the request size limit (individual endpoints may still restrict).
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = null);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// CORS runs before authentication so that preflight (OPTIONS) requests, which browsers send
// without credentials, are answered instead of being challenged with 401 Negotiate.
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapOpenApi().AllowAnonymous();

app.Run();

/// <summary>Entry point type, exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program;
