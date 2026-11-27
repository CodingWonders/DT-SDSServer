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
internal sealed class SdsCorsPolicyProvider : ICorsPolicyProvider
{
    private readonly CorsPolicy _policy;
    private readonly int _consolePort;

    public SdsCorsPolicyProvider(IConfiguration configuration)
    {
        var origins = configuration.GetSection("Sds:CorsOrigins").Get<string[]>() ?? [];
        _consolePort = configuration.GetValue("Sds:ConsolePort", 8351);
        _policy = new CorsPolicyBuilder()
            .WithOrigins(origins)
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Location", "Content-Disposition")
            .Build();
    }

    public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!_policy.Origins.Contains(origin) && IsConsoleOnApiHost(origin, context.Request.Host, _consolePort))
        {
            return Task.FromResult<CorsPolicy?>(new CorsPolicyBuilder(_policy).WithOrigins(origin).Build());
        }

        return Task.FromResult<CorsPolicy?>(_policy);
    }

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
