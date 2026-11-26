using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Sds.Core;
using Sds.Server.Security;

namespace Sds.Server.Controllers;

/// <summary>Password sign-in request.</summary>
/// <param name="UserName">DOMAIN\user, user@domain, or a local account name of the server.</param>
/// <param name="Password">Account password.</param>
public sealed record LoginRequest(string? UserName, string? Password);

/// <summary>
/// Sign-in for management front ends (such as the Web Console). Two ways are supported:
/// Windows sign-in (the browser sends Windows credentials; GET me reports the account) and
/// password sign-in (POST login checks a Windows user name and password and issues a session
/// cookie, for browsers that do not send Windows credentials). Either way only accounts in
/// the Admin or Reader role get in.
/// </summary>
[ApiController]
[Route("api/auth")]
[Authorize]
public sealed class AuthController(
    IAuthorizationService authorization,
    ICredentialValidator credentials,
    IOptions<SdsOptions> options,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Returns the caller's account, SDS role and sign-in method.</summary>
    [HttpGet("me")]
    public Task<object> Me() => DescribeAsync(User);

    /// <summary>
    /// Returns the current password session, or <c>signedIn: false</c>. Anonymous, so a front end
    /// can resume a session after a reload without starting a Windows sign-in.
    /// </summary>
    [HttpGet("session")]
    [AllowAnonymous]
    public async Task<object> Session() =>
        User.Identity is { IsAuthenticated: true, AuthenticationType: SdsAuth.PasswordScheme }
            ? await DescribeAsync(User)
            : new { signedIn = false };

    /// <summary>
    /// Checks a Windows user name and password and, for an account in the Admin or Reader role,
    /// starts a password session (HttpOnly cookie). Needs HTTPS unless the request comes from
    /// the server itself or Sds:AllowPasswordSignInOverHttp is set. Rate limited per address.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(SdsAuth.LoginRateLimit)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var o = options.Value;
        if (!o.PasswordSignIn)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Password sign-in is disabled", detail: "Password sign-in is turned off on this server (Sds:PasswordSignIn). Use Windows sign-in.");
        }

        if (!Request.Headers.ContainsKey(SdsAuth.RequestHeader))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request", detail: $"The {SdsAuth.RequestHeader} header is required.");
        }

        var remote = HttpContext.Connection.RemoteIpAddress;
        if (!Request.IsHttps && !o.AllowPasswordSignInOverHttp && !(remote is not null && IPAddress.IsLoopback(remote)))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "HTTPS required",
                detail: "Password sign-in needs HTTPS so the password is not sent in clear text. Configure an HTTPS endpoint on the SDS server, or use Windows sign-in.");
        }

        var userName = request.UserName?.Trim();
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(request.Password) || userName.Length > 256 || request.Password.Length > 256)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request", detail: "Enter a user name and a password.");
        }

        var identity = credentials.Validate(userName, request.Password);
        if (identity is null)
        {
            logger.LogWarning("Rejected password sign-in for {User} from {Address}", userName, remote);
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed", detail: "Wrong user name or password, or the account cannot sign in.");
        }

        var principal = new ClaimsPrincipal(identity);
        if (!(await authorization.AuthorizeAsync(principal, SdsPolicies.Reader)).Succeeded)
        {
            logger.LogWarning("Refused password sign-in for {User} from {Address}: not in an SDS role", identity.Name, remote);
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied",
                detail: $"{identity.Name} is not authorized to manage SDS. It must be a member of Administrators, SDS Administrators or SDS Operators.");
        }

        await HttpContext.SignInAsync(SdsAuth.PasswordScheme, principal, new AuthenticationProperties { IsPersistent = false });
        logger.LogInformation("Password sign-in for {User} from {Address}", identity.Name, remote);
        return Ok(await DescribeAsync(principal));
    }

    /// <summary>Ends the password session (deletes the cookie). Anonymous and idempotent.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(SdsAuth.PasswordScheme);
        return NoContent();
    }

    private async Task<object> DescribeAsync(ClaimsPrincipal user)
    {
        var isAdmin = (await authorization.AuthorizeAsync(user, SdsPolicies.Admin)).Succeeded;
        var isReader = isAdmin || (await authorization.AuthorizeAsync(user, SdsPolicies.Reader)).Succeeded;

        return new
        {
            signedIn = true,
            name = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name),
            role = isAdmin ? "Admin" : isReader ? "Reader" : "None",
            isAdmin,
            isReader,
            method = user.Identity?.AuthenticationType == SdsAuth.PasswordScheme ? "Password" : "Windows",
        };
    }
}
