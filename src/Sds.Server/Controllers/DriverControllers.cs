using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sds.Core.Drivers;
using Sds.Server.Infrastructure;
using Sds.Server.Security;

namespace Sds.Server.Controllers;

/// <summary>Driver packages stored in DRIVERS\&lt;drvname&gt;_&lt;arch&gt;_{GUID}.</summary>
[ApiController]
[Route("api/drivers")]
[Authorize(Policy = SdsPolicies.Reader)]
public sealed class DriversController(DriverService drivers, UploadStore uploads) : ControllerBase
{
    /// <summary>Lists driver packages with optional filters.</summary>
    [HttpGet]
    public Task<List<DriverPackageDto>> List([FromQuery] string? architecture, [FromQuery(Name = "class")] string? className, [FromQuery] string? group, [FromQuery] string? search, [FromQuery] bool? enabled, CancellationToken ct) =>
        drivers.ListAsync(architecture, className, group, search, enabled, ct);

    /// <summary>Returns one package with its hardware ids.</summary>
    [HttpGet("{id:guid}")]
    public Task<DriverPackageDto> Get(Guid id, CancellationToken ct) => drivers.GetAsync(id, ct);

    /// <summary>
    /// Imports driver packages from a folder, INF or ZIP on the server, or from an uploaded
    /// ZIP (uploadId query). Returns 202 with the job to poll.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Import(DriverImportRequest request, [FromQuery] string? uploadId, CancellationToken ct)
    {
        var effective = uploadId is null ? request : request with { SourcePath = uploads.Resolve(uploadId) };
        return JobsController.Accepted(this, await drivers.QueueImportAsync(effective, User.Identity?.Name, uploadId is not null, ct));
    }

    /// <summary>Changes the display name or enables/disables a package.</summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<DriverPackageDto> Update(Guid id, UpdateDriverRequest request, CancellationToken ct) => drivers.UpdateAsync(id, request, ct);

    /// <summary>Deletes a package.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await drivers.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Driver groups and their filters.</summary>
[ApiController]
[Route("api/driver-groups")]
[Authorize(Policy = SdsPolicies.Reader)]
public sealed class DriverGroupsController(DriverGroupService groups) : ControllerBase
{
    /// <summary>Lists driver groups.</summary>
    [HttpGet]
    public Task<List<DriverGroupDto>> List(CancellationToken ct) => groups.ListAsync(ct);

    /// <summary>Returns one driver group.</summary>
    [HttpGet("{name}")]
    public Task<DriverGroupDto> Get(string name, CancellationToken ct) => groups.GetAsync(name, ct);

    /// <summary>Creates a driver group.</summary>
    [HttpPost]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<ActionResult<DriverGroupDto>> Create(DriverGroupRequest request, CancellationToken ct)
    {
        var group = await groups.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { name = group.Name }, group);
    }

    /// <summary>Replaces a driver group (properties and filters).</summary>
    [HttpPut("{name}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<DriverGroupDto> Update(string name, DriverGroupRequest request, CancellationToken ct) => groups.UpdateAsync(name, request, ct);

    /// <summary>Deletes a driver group (packages are kept).</summary>
    [HttpDelete("{name}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Delete(string name, CancellationToken ct)
    {
        await groups.DeleteAsync(name, ct);
        return NoContent();
    }

    /// <summary>Lists the packages of a group.</summary>
    [HttpGet("{name}/packages")]
    public Task<List<DriverPackageDto>> Packages(string name, CancellationToken ct) => groups.ListPackagesAsync(name, ct);

    /// <summary>Adds packages to a group.</summary>
    [HttpPost("{name}/packages")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<DriverGroupDto> AddPackages(string name, DriverGroupMembershipRequest request, CancellationToken ct) => groups.AddPackagesAsync(name, request, ct);

    /// <summary>Removes packages from a group.</summary>
    [HttpPost("{name}/packages/remove")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<DriverGroupDto> RemovePackages(string name, DriverGroupMembershipRequest request, CancellationToken ct) => groups.RemovePackagesAsync(name, request, ct);
}
