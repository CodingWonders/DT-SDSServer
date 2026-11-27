using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sds.Core.Networking;

/// <summary>
/// Finds the HTTPS certificate created by New-SdsCertificate.ps1 (or any certificate with the
/// configured subject). Used by the server (API) and the Web Console; this file is also
/// compiled into Sds.Web (linked source), so it only depends on the base class library.
/// </summary>
public static class SdsCertificate
{
    /// <summary>Common name the script gives the certificate.</summary>
    public const string DefaultSubject = "SDS Server HTTPS";

    /// <summary>
    /// The newest certificate with subject CN=<paramref name="subject"/> that is valid now, is
    /// meant for servers and whose private key this process can open, looked up in
    /// LocalMachine\My, then CurrentUser\My. Null when there is none.
    /// </summary>
    public static (X509Certificate2 Certificate, StoreLocation Location)? Find(string? subject)
    {
        subject = string.IsNullOrWhiteSpace(subject) ? DefaultSubject : subject;
        var now = DateTime.Now;
        foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            using var store = new X509Store(StoreName.My, location);
            try
            {
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            }
            catch (CryptographicException)
            {
                continue;
            }

            var candidates = store.Certificates
                .Find(X509FindType.FindBySubjectDistinguishedName, $"CN={subject}", validOnly: false)
                .Where(c => c.NotBefore <= now && now <= c.NotAfter && IsForServers(c))
                .OrderByDescending(c => c.NotAfter);

            foreach (var certificate in candidates)
            {
                if (CanUsePrivateKey(certificate))
                {
                    return (certificate, location);
                }
            }
        }

        return null;
    }

    /// <summary>No EKU extension means any use; otherwise Server Authentication must be listed.</summary>
    private static bool IsForServers(X509Certificate2 certificate)
    {
        var eku = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        return eku is null || eku.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == "1.3.6.1.5.5.7.3.1");
    }

    /// <summary>
    /// A LocalMachine key is only readable by administrators and the accounts it was granted
    /// to; a certificate whose key this process cannot open would fail every TLS handshake.
    /// </summary>
    private static bool CanUsePrivateKey(X509Certificate2 certificate)
    {
        try
        {
            using var key = (AsymmetricAlgorithm?)certificate.GetRSAPrivateKey() ?? certificate.GetECDsaPrivateKey();
            return key is not null;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
