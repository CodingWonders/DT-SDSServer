using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sds.Core.Computers;
using Sds.Data.Entities;
using Sds.Server.Security;

namespace Sds.Server.Controllers;

/// <summary>Known computers (WDS prestaged devices).</summary>
[ApiController]
[Route("api/computers")]
[Authorize(Policy = SdsPolicies.Reader)]
public sealed class ComputersController(ComputerService computers) : ControllerBase
{
    /// <summary>Lists computers; search matches name or MAC.</summary>
    [HttpGet]
    public Task<List<ComputerDto>> List([FromQuery] string? search, CancellationToken ct) => computers.ListAsync(search, ct);

    /// <summary>Returns one computer.</summary>
    [HttpGet("{id:int}")]
    public Task<ComputerDto> Get(int id, CancellationToken ct) => computers.GetAsync(id, ct);

    /// <summary>Prestages a computer (authorizes it: it becomes a known client).</summary>
    [HttpPost]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<ActionResult<ComputerDto>> Create(ComputerRequest request, CancellationToken ct)
    {
        var computer = await computers.CreateAsync(request, ComputerOrigin.Prestaged, ct);
        return CreatedAtAction(nameof(Get), new { id = computer.Id }, computer);
    }

    /// <summary>Replaces the properties of a computer.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<ComputerDto> Update(int id, ComputerRequest request, CancellationToken ct) => computers.UpdateAsync(id, request, ct);

    /// <summary>Deletes a computer (de-authorizes it); deleteFromDirectory also removes the AD object.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool deleteFromDirectory, CancellationToken ct)
    {
        await computers.DeleteAsync(id, deleteFromDirectory, ct);
        return NoContent();
    }

    /// <summary>Pushes the computer to Active Directory again.</summary>
    [HttpPost("{id:int}/sync")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<ComputerDto> Sync(int id, CancellationToken ct) => computers.SyncAsync(id, ct);

    /// <summary>Returns the device specific client unattend (404 when none).</summary>
    [HttpGet("{id:int}/unattend")]
    public async Task<IActionResult> Unattend(int id, CancellationToken ct)
    {
        var xml = await computers.GetClientUnattendAsync(id, ct);
        return xml is null ? NotFound() : Content(xml, "application/xml");
    }
}

/// <summary>Devices waiting for administrator approval (WDS "Pending Devices").</summary>
[ApiController]
[Route("api/pending-devices")]
[Authorize(Policy = SdsPolicies.Reader)]
public sealed class PendingDevicesController(PendingDeviceService pending) : ControllerBase
{
    /// <summary>Lists pending device records (status filter: Pending, Approved, Rejected).</summary>
    [HttpGet]
    public Task<List<PendingDeviceDto>> List([FromQuery] PendingDeviceStatus? status, CancellationToken ct) => pending.ListAsync(status, ct);

    /// <summary>Returns one record.</summary>
    [HttpGet("{id:long}")]
    public Task<PendingDeviceDto> Get(long id, CancellationToken ct) => pending.GetAsync(id, ct);

    /// <summary>Approves a device; it becomes a known computer.</summary>
    [HttpPost("{id:long}/approve")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<PendingDeviceDto> Approve(long id, ApproveRequest request, CancellationToken ct) => pending.ApproveAsync(id, request, User.Identity?.Name, ct);

    /// <summary>Approves every pending device with the same options.</summary>
    [HttpPost("approve-all")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<List<PendingDeviceDto>> ApproveAll(ApproveRequest request, CancellationToken ct) => pending.ApproveAllAsync(request, User.Identity?.Name, ct);

    /// <summary>Rejects a device.</summary>
    [HttpPost("{id:long}/reject")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<PendingDeviceDto> Reject(long id, RejectRequest request, CancellationToken ct) => pending.RejectAsync(id, request, User.Identity?.Name, ct);

    /// <summary>Deletes one record.</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await pending.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Deletes every record, or only those with the given status.</summary>
    [HttpDelete]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> DeleteAll([FromQuery] PendingDeviceStatus? status, CancellationToken ct) =>
        Ok(new { deleted = await pending.DeleteAllAsync(status, ct) });
}
