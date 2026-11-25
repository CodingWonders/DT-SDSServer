using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sds.Core.Computers;
using Sds.Core.Drivers;
using Sds.Core.Images;
using Sds.Core.Logging;
using Sds.Core.Multicast;
using Sds.Core.Util;

namespace Sds.Server.Controllers;

/// <summary>
/// Endpoints called by deployment clients (the equivalent of the WDS client protocol).
/// </summary>
/// <remarks>
/// Typical flow of a client booted from the SDS boot image:
/// <list type="number">
///   <item><description>POST register (anonymous): Known, Allowed, Pending, Rejected or Ignored.</description></item>
///   <item><description>While Pending, GET requests/{id} every PollIntervalSeconds.</description></item>
///   <item><description>GET unattend/{arch} (anonymous) for the WDS client unattend.</description></item>
///   <item><description>With user credentials: GET images, POST name, POST drivers/query.</description></item>
///   <item><description>Apply the image from the UNC paths (metadata WIM + Res.RWM reference).</description></item>
///   <item><description>POST logs along the way (anonymous).</description></item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/client")]
public sealed class ClientController(
    ClientAuthorizationService authorization,
    ComputerService computers,
    ImageService images,
    DriverGroupService driverGroups,
    ClientLogService logs,
    MulticastService multicast) : ControllerBase
{
    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Registers a booting client and returns the server decision.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public Task<ClientDecision> Register(ClientRegisterRequest request, CancellationToken ct) => authorization.RegisterAsync(request, Ip, ct);

    /// <summary>Polls a pending approval request.</summary>
    [HttpGet("requests/{id:long}")]
    [AllowAnonymous]
    public Task<ClientDecision> Poll(long id, CancellationToken ct) => authorization.PollAsync(id, ct);

    /// <summary>
    /// Returns the WDS client unattend: the device specific one when the MAC or GUID belongs to
    /// a known computer, otherwise the server default for the architecture.
    /// </summary>
    [HttpGet("unattend/{architecture}")]
    [AllowAnonymous]
    public async Task<IActionResult> Unattend(string architecture, [FromQuery] string? mac, [FromQuery] string? guid, CancellationToken ct)
    {
        var arch = Identifiers.NormalizeArchitecture(architecture)!;
        var known = mac is null && guid is null ? null : await computers.FindAsync(Identifiers.NormalizeMac(mac), Identifiers.NormalizeGuid(guid), ct);
        var xml = known?.ClientUnattend ?? await authorization.GetServerClientUnattendAsync(arch, ct);
        return xml is null ? NotFound() : Content(xml, "application/xml");
    }

    /// <summary>
    /// Images the authenticated user may install, filtered by architecture and by the image
    /// file permissions (WDS user filter).
    /// </summary>
    [HttpGet("images")]
    [Authorize]
    public Task<List<ClientImageDto>> Images([FromQuery] string? architecture, [FromQuery] string? group, CancellationToken ct) =>
        images.ListForClientAsync(User.Identity as WindowsIdentity, Identifiers.NormalizeArchitecture(architecture), group, ct);

    /// <summary>
    /// Generates a computer name with the naming policy for the authenticated user (used by
    /// clients allowed without prestaging).
    /// </summary>
    [HttpPost("name")]
    [Authorize]
    public async Task<object> Name(GenerateNameRequest request, CancellationToken ct) => new
    {
        name = await computers.GenerateNameAsync(new NamingValues(request.First, request.Last, request.Username ?? User.Identity?.Name, Identifiers.NormalizeMac(request.MacAddress)), ct),
    };

    /// <summary>Returns the driver packages applicable to the client (driver group evaluation).</summary>
    [HttpPost("drivers/query")]
    [Authorize]
    public Task<List<DriverMatchDto>> Drivers(DriverQueryRequest request, CancellationToken ct) => driverGroups.QueryAsync(request, ct);

    /// <summary>Receives a client log entry.</summary>
    [HttpPost("logs")]
    [AllowAnonymous]
    public async Task<object> Log(ClientLogRequest request, CancellationToken ct) => new { stored = await logs.AddAsync(request, Ip, ct) };

    /// <summary>Joins the multicast transmission of an image.</summary>
    [HttpPost("multicast/{imageId:int}/join")]
    [Authorize]
    public Task<MulticastDto> Join(int imageId, MulticastJoinRequest request, CancellationToken ct) => multicast.JoinAsync(imageId, request, Ip, ct);

    /// <summary>Reports multicast progress, or leaves the transmission.</summary>
    [HttpPost("multicast/{id:int}/clients/{clientId:int}")]
    [Authorize]
    public async Task<IActionResult> Progress(int id, int clientId, [FromQuery] int percent, [FromQuery] bool leave, CancellationToken ct)
    {
        await multicast.ReportAsync(id, clientId, percent, leave, ct);
        return NoContent();
    }
}
