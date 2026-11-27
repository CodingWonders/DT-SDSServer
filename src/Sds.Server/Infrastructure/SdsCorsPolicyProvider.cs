using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Sds.Server.Infrastructure;

/// <summary>
/// CORS policy for browser front ends. Allows the origins listed in <c>Sds:CorsOrigins</c>,
/// plus the Web Console served from this same machine: an origin on port
/// <c>Sds:ConsolePort</c> whose host is the host name the browser used to reach the API.
/// That way the console works whether it is opened as localhost, by IP address or by machine
/// name, without listing every name. Credentials require explicit origins (no wildcard), so
/// the matching origin is echoed back per request.
/// </summary>
public sealed class SdsCorsPolicyProvider : ICorsPolicyProvider
{
    private readonly string[] _origins;
    private readonly CorsPolicy _policy;
    private readonly int _consolePort;

    public SdsCorsPolicyProvider(IConfiguration configuration)
    {
        _origins = configuration.GetSection("Sds:CorsOrigins").Get<string[]>() ?? [];
        _consolePort = configuration.GetValue("Sds:ConsolePort", 8351);
        _policy = Build(_origins);
    }

    /// <summary>
    /// Whether a browser page from <paramref name="origin"/> may use the API reached as
    /// <paramref name="apiHost"/>: a listed origin, or the console on the API's own host.
    /// </summary>
    public bool IsAllowedOrigin(string origin, HostString apiHost) =>
        _policy.IsOriginAllowed(origin) || IsConsoleOnApiHost(origin, apiHost, _consolePort);

    public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!_policy.IsOriginAllowed(origin) && IsConsoleOnApiHost(origin, context.Request.Host, _consolePort))
        {
            // Built from scratch on purpose: new CorsPolicyBuilder(_policy) would copy the
            // IsOriginAllowed delegate, which still checks _policy's origin list, so the added
            // origin would be ignored.
            return Task.FromResult<CorsPolicy?>(Build([.. _origins, origin]));
        }

        return Task.FromResult<CorsPolicy?>(_policy);
    }

    private static CorsPolicy Build(string[] origins) => new CorsPolicyBuilder()
        .WithOrigins(origins)
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("Location", "Content-Disposition")
        .Build();

    /// <summary>
    /// True when <paramref name="origin"/> is http(s)://&lt;API host&gt;:&lt;console port&gt;,
    /// the API host being the one in the request's Host header. A port of 0 turns this off.
    /// </summary>
    internal static bool IsConsoleOnApiHost(string origin, HostString apiHost, int consolePort)
    {
        if (consolePort <= 0 || !apiHost.HasValue
            || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.Port != consolePort
            || uri.AbsolutePath != "/")
        {
            return false;
        }

        static string Bare(string host) => host.Trim('[', ']');
        return string.Equals(Bare(uri.Host), Bare(apiHost.Host), StringComparison.OrdinalIgnoreCase);
    }
}
