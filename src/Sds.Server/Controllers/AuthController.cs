using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sds.Server.Security;

namespace Sds.Server.Controllers;

/// <summary>
/// Identity of the caller. Management front ends (such as the Web Console) call this first to
/// sign in: it tells them who the Windows account is and which SDS role it holds, so they can
/// refuse accounts that are authenticated but not authorized before showing anything else.
/// </summary>
[ApiController]
[Route("api/auth")]
[Authorize]
public sealed class AuthController(IAuthorizationService authorization) : ControllerBase
{
    /// <summary>
    /// Returns the caller's account name and SDS role: <c>Admin</c>, <c>Reader</c>, or
    /// <c>None</c> for an authenticated account that is in neither role.
    /// </summary>
    [HttpGet("me")]
    public async Task<object> Me()
    {
        var isAdmin = (await authorization.AuthorizeAsync(User, SdsPolicies.Admin)).Succeeded;
        var isReader = isAdmin || (await authorization.AuthorizeAsync(User, SdsPolicies.Reader)).Succeeded;

        return new
        {
            name = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name),
            role = isAdmin ? "Admin" : isReader ? "Reader" : "None",
            isAdmin,
            isReader,
        };
    }
}
