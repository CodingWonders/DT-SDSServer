using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Sds.Server.Infrastructure;

/// <summary>
/// Turns on the HTTPS endpoint by itself when the server's certificate is installed, so that
/// running New-SdsCertificate.ps1 and restarting the server is all it takes.
///
/// Steps:
///   1. Skip when Sds:HttpsPort is 0, or when Kestrel:Endpoints is configured (the
///      administrator's own endpoint settings always win).
///   2. Find the newest valid certificate with subject CN=&lt;Sds:HttpsCertificateSubject&gt;
///      (default "SDS Server HTTPS") and a usable private key, in LocalMachine\My, then
///      CurrentUser\My.
///   3. Add Kestrel:Endpoints for every HTTP address of "urls" (Kestrel endpoints replace
///      "urls") plus https://*:&lt;port&gt;, and make that certificate, limited to TLS 1.2 and
///      1.3, the default for HTTPS endpoints.
/// The firewall rule reads the same Kestrel:Endpoints, so it covers the HTTPS port too.
/// </summary>
internal static class AutomaticHttps
{
    public const string DefaultSubject = Sds.Core.Networking.SdsCertificate.DefaultSubject;
    public const int DefaultPort = 8352;

    /// <summary>Kestrel's own default when no address is configured.</summary>
    private const string KestrelDefaultUrl = "http://localhost:5000";

    /// <summary>
    /// Applies the steps above to <paramref name="builder"/> and returns what happened, to be
    /// logged once logging is up.
    /// </summary>
    public static string Configure(WebApplicationBuilder builder) =>
        Configure(builder.Configuration, Sds.Core.Networking.SdsCertificate.Find, (certificate, protocols) =>
            builder.WebHost.ConfigureKestrel(k => k.ConfigureHttpsDefaults(https =>
            {
                https.ServerCertificate = certificate;
                https.SslProtocols = protocols;
            })));

    /// <summary>Testable core: the certificate lookup and the Kestrel hook are passed in.</summary>
    internal static string Configure(
        ConfigurationManager configuration,
        Func<string, (X509Certificate2 Certificate, StoreLocation Location)?> findCertificate,
        Action<X509Certificate2, SslProtocols> useCertificate)
    {
        // Step 1
        var port = configuration.GetValue("Sds:HttpsPort", DefaultPort);
        if (port <= 0)
        {
            return "Automatic HTTPS is turned off (Sds:HttpsPort is 0)";
        }

        if (configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
        {
            return "Kestrel:Endpoints is configured, so no HTTPS endpoint was added automatically";
        }

        // Step 2
        var subject = configuration["Sds:HttpsCertificateSubject"] is { Length: > 0 } configured ? configured : DefaultSubject;
        if (findCertificate(subject) is not { } found)
        {
            return $"HTTPS is off: no valid certificate CN={subject} with a usable private key in LocalMachine\\My or CurrentUser\\My. Run New-SdsCertificate.ps1 (next to SdsServer.exe) and restart the server to turn it on.";
        }

        // Step 3
        var urls = (configuration["urls"] ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (urls.Length == 0)
        {
            urls = [KestrelDefaultUrl];
        }

        var endpoints = new Dictionary<string, string?>();
        for (var i = 0; i < urls.Length; i++)
        {
            endpoints[$"Kestrel:Endpoints:Http{i}:Url"] = urls[i];
        }

        endpoints["Kestrel:Endpoints:AutoHttps:Url"] = $"https://*:{port}";
        configuration.AddInMemoryCollection(endpoints);
        useCertificate(found.Certificate, SslProtocols.Tls12 | SslProtocols.Tls13);

        // Kestrel then logs "Overriding address(es) ..." for "urls": expected, the same
        // addresses are bound through the endpoints above.
        return $"HTTPS is on: port {port} with certificate CN={subject} ({found.Certificate.Thumbprint}, {found.Location}\\My, valid until {found.Certificate.NotAfter:yyyy-MM-dd})";
    }
}
