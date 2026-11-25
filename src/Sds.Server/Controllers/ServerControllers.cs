using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sds.Core.Computers;
using Sds.Core.Logging;
using Sds.Core.Maintenance;
using Sds.Core.Multicast;
using Sds.Core.Settings;
using Sds.Core.Storage;
using Sds.Data;
using Sds.Data.Entities;
using Sds.DirectoryServices;
using Sds.Server.Security;

namespace Sds.Server.Controllers;

/// <summary>Server information, settings, reconcile and server wide client unattend.</summary>
[ApiController]
[Route("api/server")]
[Authorize(Policy = SdsPolicies.Reader)]
public sealed class ServerController(
    ShareLayout layout,
    SettingsService settings,
    Reconciler reconciler,
    ClientAuthorizationService clients,
    IComputerDirectory directory,
    IDbContextFactory<SdsDbContext> dbFactory) : ControllerBase
{
    /// <summary>Server status and statistics.</summary>
    [HttpGet]
    public async Task<object> Info(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return new
        {
            server = layout.ServerName,
            version = typeof(ServerController).Assembly.GetName().Version?.ToString(),
            root = layout.Root,
            shares = new { images = layout.ImagesUnc, drivers = layout.DriversUnc },
            folders = new { pxeCore = layout.PxeCore, images = layout.Images, drivers = layout.Drivers },
            dataDirectory = layout.DataDirectory,
            activeDirectory = directory.IsExternal,
            counts = new
            {
                imageGroups = await db.ImageGroups.CountAsync(ct),
                images = await db.Images.CountAsync(ct),
                driverPackages = await db.DriverPackages.CountAsync(ct),
                driverGroups = await db.DriverGroups.CountAsync(ct),
                computers = await db.Computers.CountAsync(ct),
                pendingDevices = await db.PendingDevices.CountAsync(p => p.Status == PendingDeviceStatus.Pending, ct),
                multicastTransmissions = await db.MulticastTransmissions.CountAsync(ct),
            },
        };
    }

    /// <summary>Returns the server policy settings.</summary>
    [HttpGet("settings")]
    public Task<ServerSettings> GetSettings(CancellationToken ct) => settings.GetAsync(ct);

    /// <summary>Replaces the server policy settings.</summary>
    [HttpPut("settings")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<ServerSettings> SaveSettings(ServerSettings value, CancellationToken ct) => settings.SaveAsync(value, ct);

    /// <summary>Runs a database and share consistency pass.</summary>
    [HttpPost("reconcile")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<ReconcileReport> Reconcile(CancellationToken ct) => reconciler.RunAsync(ct);

    /// <summary>Returns the server wide client unattend for an architecture (404 when none).</summary>
    [HttpGet("client-unattend/{architecture}")]
    public async Task<IActionResult> GetClientUnattend(string architecture, CancellationToken ct)
    {
        var xml = await clients.GetServerClientUnattendAsync(Sds.Core.Util.Identifiers.NormalizeArchitecture(architecture)!, ct);
        return xml is null ? NotFound() : Content(xml, "application/xml");
    }

    /// <summary>Sets the server wide client unattend for an architecture (raw XML body).</summary>
    [HttpPut("client-unattend/{architecture}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    [Consumes("application/xml", "text/xml", "text/plain")]
    public async Task<IActionResult> SetClientUnattend(string architecture, CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        await clients.SetServerClientUnattendAsync(architecture, await reader.ReadToEndAsync(ct), ct);
        return NoContent();
    }

    /// <summary>Removes the server wide client unattend for an architecture.</summary>
    [HttpDelete("client-unattend/{architecture}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> DeleteClientUnattend(string architecture, CancellationToken ct)
    {
        await clients.SetServerClientUnattendAsync(architecture, null, ct);
        return NoContent();
    }
}

/// <summary>Multicast transmissions (management only).</summary>
[ApiController]
[Route("api/multicast")]
[Authorize(Policy = SdsPolicies.Reader)]
public sealed class MulticastController(MulticastService multicast) : ControllerBase
{
    /// <summary>Lists transmissions.</summary>
    [HttpGet]
    public Task<List<MulticastDto>> List(CancellationToken ct) => multicast.ListAsync(ct);

    /// <summary>Returns one transmission with its clients.</summary>
    [HttpGet("{id:int}")]
    public Task<MulticastDto> Get(int id, CancellationToken ct) => multicast.GetAsync(id, ct);

    /// <summary>Creates an AutoCast or ScheduledCast transmission.</summary>
    [HttpPost]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<ActionResult<MulticastDto>> Create(MulticastRequest request, CancellationToken ct)
    {
        var t = await multicast.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = t.Id }, t);
    }

    /// <summary>Starts a transmission immediately.</summary>
    [HttpPost("{id:int}/start")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<MulticastDto> Start(int id, CancellationToken ct) => multicast.StartAsync(id, ct);

    /// <summary>Stops a transmission.</summary>
    [HttpPost("{id:int}/stop")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<MulticastDto> Stop(int id, CancellationToken ct) => multicast.StopAsync(id, ct);

    /// <summary>Deletes a transmission.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await multicast.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Disconnects a client from a transmission.</summary>
    [HttpDelete("{id:int}/clients/{clientId:int}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Disconnect(int id, int clientId, CancellationToken ct)
    {
        await multicast.DisconnectClientAsync(id, clientId, ct);
        return NoContent();
    }
}

/// <summary>Client log entries (WDS client logging).</summary>
[ApiController]
[Route("api/client-logs")]
[Authorize(Policy = SdsPolicies.Reader)]
public sealed class ClientLogsController(ClientLogService logs) : ControllerBase
{
    /// <summary>Queries client log entries, newest first.</summary>
    [HttpGet]
    public Task<List<ClientLogEntry>> Query([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? mac, [FromQuery] string? computer, [FromQuery] ClientLogLevel? level, [FromQuery] int take = 500, CancellationToken ct = default) =>
        logs.QueryAsync(from, to, mac, computer, level, take, ct);
}
