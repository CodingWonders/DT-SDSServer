using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sds.Server.Infrastructure;

namespace Sds.Server.Controllers;

/// <summary>
/// Lets a browser accept the server's self-signed certificate on its way to the Web Console.
///
/// A page cannot show the "accept this certificate" prompt for a fetch() call: the call just
/// fails. So when the console cannot reach the API over HTTPS, it sends the browser here as a
/// normal page visit. The browser shows its certificate warning for the API address, the user
/// accepts it, and this endpoint sends the browser straight back to the console, which can now
/// call the API.
///
/// Only addresses the API's CORS policy already allows are redirected to (Sds:CorsOrigins, or
/// the console on this machine), so this is not an open redirect.
/// </summary>
[ApiController]
[Route("api/trust")]
[AllowAnonymous]
public sealed class TrustController(SdsCorsPolicyProvider cors) : ControllerBase
{
    /// <summary>
    /// Redirects to <paramref name="returnUrl"/> when its origin may use the API. Without a
    /// return address, answers 204 (the certificate is accepted at this point).
    /// </summary>
    [HttpGet]
    public IActionResult Get([FromQuery(Name = "return")] string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            return NoContent();
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.UserInfo.Length > 0
            || !cors.IsAllowedOrigin(uri.GetLeftPart(UriPartial.Authority), Request.Host))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Return address not allowed",
                detail: "The return address must be a page allowed to use this API (Sds:CorsOrigins, or the Web Console on this server).");
        }

        return Redirect(uri.AbsoluteUri);
    }
}
