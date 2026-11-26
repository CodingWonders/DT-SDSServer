using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Win32.SafeHandles;

namespace Sds.Server.Security;

/// <summary>
/// Names shared by the two ways of signing in: Windows Negotiate (the browser or client sends
/// Windows credentials) and password sign-in (POST /api/auth/login checks a Windows user name
/// and password once and issues a session cookie).
/// </summary>
public static class SdsAuth
{
    /// <summary>Default scheme: forwards each request to Negotiate or to the session cookie.</summary>
    public const string Scheme = "Sds";

    /// <summary>Cookie scheme of password sessions. Also the AuthenticationType of their identity.</summary>
    public const string PasswordScheme = "SdsPassword";

    /// <summary>Name of the session cookie.</summary>
    public const string CookieName = "sds.session";

    /// <summary>
    /// Header that cookie authenticated writes and the login call must carry. Browsers only send
    /// a custom header cross origin after a CORS preflight, which only Sds:CorsOrigins pass, so
    /// other sites cannot forge requests that ride on the session cookie (CSRF).
    /// </summary>
    public const string RequestHeader = "X-SDS-Request";

    /// <summary>Rate limiter policy of the login endpoint.</summary>
    public const string LoginRateLimit = "sds-login";

    /// <summary>
    /// Picks the scheme of a request: the session cookie when the request carries it and no
    /// Authorization header, otherwise Windows (Negotiate, or the test scheme in tests).
    /// </summary>
    public static string Select(HttpContext context, string windowsScheme) =>
        !context.Request.Headers.ContainsKey("Authorization") && context.Request.Cookies.ContainsKey(CookieName)
            ? PasswordScheme
            : windowsScheme;

    /// <summary>Session cookie settings: HttpOnly, sliding lifetime, API style 401/403 instead of redirects.</summary>
    public static void ConfigureCookie(CookieAuthenticationOptions o, int sessionMinutes)
    {
        o.Cookie.Name = CookieName;
        o.Cookie.HttpOnly = true;
        o.Cookie.IsEssential = true;

        // Lax over HTTP (console and API on the same host count as the same site, whatever the
        // port). Over HTTPS the cookie policy in Program.cs switches to SameSite=None; Secure so a
        // console on another host works too.
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromMinutes(Math.Max(5, sessionMinutes));
        o.SlidingExpiration = true;

        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };

        // CSRF guard: a write that rides on the cookie must carry the custom header.
        o.Events.OnValidatePrincipal = ctx =>
        {
            var method = ctx.Request.Method;
            var safe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
            if (!safe && !ctx.Request.Headers.ContainsKey(RequestHeader))
            {
                ctx.RejectPrincipal();
            }

            return Task.CompletedTask;
        };
    }
}

/// <summary>Checks a Windows user name and password.</summary>
public interface ICredentialValidator
{
    /// <summary>
    /// Returns the identity (name, user SID and group SIDs) of a valid account, or null when the
    /// user name or password is wrong or the account cannot sign in.
    /// </summary>
    ClaimsIdentity? Validate(string userName, string password);
}

/// <summary>
/// Validates credentials with LogonUser (network logon, the same kind of logon Negotiate
/// produces) and copies the groups of the resulting token into claims, so the role checks of
/// <see cref="SdsRoleHandler"/> work exactly as for Windows sign-in.
/// </summary>
public sealed class WindowsCredentialValidator(ILogger<WindowsCredentialValidator> logger) : ICredentialValidator
{
    private const int Logon32LogonNetwork = 3;
    private const int Logon32ProviderDefault = 0;

    /// <inheritdoc />
    public ClaimsIdentity? Validate(string userName, string password)
    {
        // "DOMAIN\user" or "user@domain"; a bare name is a local account of the server.
        string user = userName;
        string? domain = ".";
        var slash = userName.IndexOf('\\');
        if (slash > 0)
        {
            domain = userName[..slash];
            user = userName[(slash + 1)..];
        }
        else if (userName.Contains('@'))
        {
            domain = null;
        }

        if (!LogonUser(user, domain, password, Logon32LogonNetwork, Logon32ProviderDefault, out var token))
        {
            var error = Marshal.GetLastWin32Error();
            logger.LogWarning("Password sign-in failed for {User}: {Reason} ({Error})", userName, new Win32Exception(error).Message, error);
            return null;
        }

        using (token)
        {
            using var windows = new WindowsIdentity(token.DangerousGetHandle());
            var claims = new List<Claim> { new(ClaimTypes.Name, windows.Name) };
            if (windows.User is not null)
            {
                claims.Add(new Claim(ClaimTypes.PrimarySid, windows.User.Value));
            }

            foreach (var group in windows.Groups ?? [])
            {
                claims.Add(new Claim(ClaimTypes.GroupSid, group.Value));
            }

            return new ClaimsIdentity(claims, SdsAuth.PasswordScheme, ClaimTypes.Name, ClaimTypes.Role);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LogonUser(string lpszUsername, string? lpszDomain, string lpszPassword, int dwLogonType, int dwLogonProvider, out SafeAccessTokenHandle phToken);
}
