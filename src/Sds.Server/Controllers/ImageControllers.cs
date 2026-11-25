using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sds.Core.Images;
using Sds.Core.Security;
using Sds.Data.Entities;
using Sds.Imaging;
using Sds.Server.Infrastructure;
using Sds.Server.Security;

namespace Sds.Server.Controllers;

/// <summary>
/// Install image groups: IMAGES\&lt;group&gt; folders with their Res.RWM.
/// </summary>
[ApiController]
[Route("api/image-groups")]
[Authorize(Policy = SdsPolicies.Reader)]
public sealed class ImageGroupsController(ImageService images, UploadStore uploads) : ControllerBase
{
    /// <summary>Lists every image group.</summary>
    [HttpGet]
    public Task<List<ImageGroupDto>> List(CancellationToken ct) => images.ListGroupsAsync(ct);

    /// <summary>Returns one image group.</summary>
    [HttpGet("{name}")]
    public Task<ImageGroupDto> Get(string name, CancellationToken ct) => images.GetGroupAsync(name, ct);

    /// <summary>Creates an image group.</summary>
    [HttpPost]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<ActionResult<ImageGroupDto>> Create(CreateImageGroupRequest request, CancellationToken ct)
    {
        var group = await images.CreateGroupAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { name = group.Name }, group);
    }

    /// <summary>Renames a group or changes its description.</summary>
    [HttpPatch("{name}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<ImageGroupDto> Update(string name, UpdateImageGroupRequest request, CancellationToken ct) => images.UpdateGroupAsync(name, request, ct);

    /// <summary>Deletes a group; with force=true also deletes its images.</summary>
    [HttpDelete("{name}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Delete(string name, [FromQuery] bool force, CancellationToken ct)
    {
        await images.DeleteGroupAsync(name, force, ct);
        return NoContent();
    }

    /// <summary>Returns the NTFS permissions of the group folder (group level user filter).</summary>
    [HttpGet("{name}/acl")]
    public Task<AclDto> GetAcl(string name, CancellationToken ct) => images.GetGroupAclAsync(name, ct);

    /// <summary>Replaces the NTFS permissions of the group folder.</summary>
    [HttpPut("{name}/acl")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<AclDto> SetAcl(string name, AclDto acl, CancellationToken ct) => images.SetGroupAclAsync(name, acl, ct);

    /// <summary>Rebuilds Res.RWM to reclaim the space of deleted images (background job).</summary>
    [HttpPost("{name}/compact")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Compact(string name, CancellationToken ct) =>
        JobsController.Accepted(this, await images.QueueCompactAsync(name, User.Identity?.Name, ct));

    /// <summary>Lists the images of a group (priority order).</summary>
    [HttpGet("{name}/images")]
    public Task<List<ImageDto>> ListImages(string name, CancellationToken ct) => images.ListImagesAsync(name, ct);

    /// <summary>
    /// Adds install images to the group (WDS "Add Install Image"). The source is a server side
    /// path in the body, or a file previously sent to POST /api/uploads (uploadId query).
    /// Returns 202 with the job to poll.
    /// </summary>
    [HttpPost("{name}/images")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> AddImages(string name, AddImageRequest request, [FromQuery] string? uploadId, CancellationToken ct)
    {
        var effective = uploadId is null ? request : request with { SourcePath = uploads.Resolve(uploadId) };
        var job = await images.QueueAddImagesAsync(name, effective, User.Identity?.Name, deleteSourceWhenDone: uploadId is not null, ct);
        return JobsController.Accepted(this, job);
    }
}

/// <summary>Install images.</summary>
[ApiController]
[Route("api/images")]
[Authorize(Policy = SdsPolicies.Reader)]
public sealed class ImagesController(ImageService images, UploadStore uploads) : ControllerBase
{
    /// <summary>Lists all images, optionally of one group.</summary>
    [HttpGet]
    public Task<List<ImageDto>> List([FromQuery] string? group, CancellationToken ct) => images.ListImagesAsync(group, ct);

    /// <summary>Returns one image.</summary>
    [HttpGet("{id:int}")]
    public Task<ImageDto> Get(int id, CancellationToken ct) => images.GetImageAsync(id, ct);

    /// <summary>Lists the images inside a source WIM before adding them.</summary>
    [HttpPost("inspect")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<IReadOnlyList<WimImageInfo>> Inspect([FromBody] InspectRequest request, [FromQuery] string? uploadId, CancellationToken ct) =>
        images.InspectSourceAsync(uploadId is null ? request.SourcePath ?? string.Empty : uploads.Resolve(uploadId), ct);

    /// <summary>Changes name, description, enabled state or priority.</summary>
    [HttpPatch("{id:int}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<ImageDto> Update(int id, UpdateImageRequest request, CancellationToken ct) => images.UpdateImageAsync(id, request, ct);

    /// <summary>Deletes an image.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await images.DeleteImageAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Exports the image to a standalone WIM (WDS "Export Image"), to a server path or to the
    /// staging area for download through GET /api/jobs/{jobId}/file.
    /// </summary>
    [HttpPost("{id:int}/export")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Export(int id, ExportImageRequest request, CancellationToken ct) =>
        JobsController.Accepted(this, await images.QueueExportAsync(id, request, User.Identity?.Name, ct));

    /// <summary>Copies the image to another group.</summary>
    [HttpPost("{id:int}/copy")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Copy(int id, CopyImageRequest request, CancellationToken ct) =>
        JobsController.Accepted(this, await images.QueueCopyAsync(id, request, move: false, User.Identity?.Name, ct));

    /// <summary>Moves the image to another group.</summary>
    [HttpPost("{id:int}/move")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Move(int id, CopyImageRequest request, CancellationToken ct) =>
        JobsController.Accepted(this, await images.QueueCopyAsync(id, request, move: true, User.Identity?.Name, ct));

    /// <summary>Replaces the image content (WDS "Replace Image").</summary>
    [HttpPost("{id:int}/replace")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> Replace(int id, ReplaceImageRequest request, [FromQuery] string? uploadId, CancellationToken ct)
    {
        var effective = uploadId is null ? request : request with { SourcePath = uploads.Resolve(uploadId) };
        return JobsController.Accepted(this, await images.QueueReplaceAsync(id, effective, User.Identity?.Name, uploadId is not null, ct));
    }

    /// <summary>Returns the image unattend XML (404 when none).</summary>
    [HttpGet("{id:int}/unattend")]
    public async Task<IActionResult> GetUnattend(int id, CancellationToken ct)
    {
        var xml = await images.GetUnattendAsync(id, ct);
        return xml is null ? NotFound() : Content(xml, "application/xml");
    }

    /// <summary>Sets the image unattend XML (raw XML body).</summary>
    [HttpPut("{id:int}/unattend")]
    [Authorize(Policy = SdsPolicies.Admin)]
    [Consumes("application/xml", "text/xml", "text/plain")]
    public async Task<IActionResult> SetUnattend(int id, CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        await images.SetUnattendAsync(id, await reader.ReadToEndAsync(ct), ct);
        return NoContent();
    }

    /// <summary>Removes the image unattend file.</summary>
    [HttpDelete("{id:int}/unattend")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> DeleteUnattend(int id, CancellationToken ct)
    {
        await images.SetUnattendAsync(id, null, ct);
        return NoContent();
    }

    /// <summary>Returns the NTFS permissions of the image file (WDS user filter).</summary>
    [HttpGet("{id:int}/acl")]
    public Task<AclDto> GetAcl(int id, CancellationToken ct) => images.GetImageAclAsync(id, ct);

    /// <summary>Replaces the NTFS permissions of the image file (WDS user filter).</summary>
    [HttpPut("{id:int}/acl")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public Task<AclDto> SetAcl(int id, AclDto acl, CancellationToken ct) => images.SetImageAclAsync(id, acl, ct);
}

/// <summary>Body of POST /api/images/inspect.</summary>
/// <param name="SourcePath">Server side WIM path (ignored when uploadId is given).</param>
public sealed record InspectRequest(string? SourcePath);

/// <summary>Raw file uploads used as sources for images and drivers.</summary>
[ApiController]
[Route("api/uploads")]
[Authorize(Policy = SdsPolicies.Admin)]
public sealed class UploadsController(UploadStore store) : ControllerBase
{
    /// <summary>
    /// Uploads a file as the raw request body (Content-Type application/octet-stream).
    /// Returns the upload id to pass as uploadId to add image, replace image or driver import.
    /// </summary>
    [HttpPost]
    [DisableRequestSizeLimit]
    [Consumes("application/octet-stream")]
    public async Task<IActionResult> Upload([FromQuery] string fileName, CancellationToken ct)
    {
        var (id, _, size) = await store.SaveAsync(Request.Body, fileName, ct);
        return Ok(new { uploadId = id, fileName, size });
    }

    /// <summary>Deletes an upload that is no longer needed.</summary>
    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        store.Delete(id);
        return NoContent();
    }
}

/// <summary>Background jobs.</summary>
[ApiController]
[Route("api/jobs")]
[Authorize(Policy = SdsPolicies.Reader)]
public sealed class JobsController(Sds.Core.Jobs.JobService jobs) : ControllerBase
{
    /// <summary>Lists recent jobs.</summary>
    [HttpGet]
    public Task<List<Job>> List([FromQuery] int take = 100, CancellationToken ct = default) => jobs.ListAsync(take, ct);

    /// <summary>Returns a job (status, progress, result).</summary>
    [HttpGet("{id:guid}")]
    public Task<Job> Get(Guid id, CancellationToken ct) => jobs.GetAsync(id, ct);

    /// <summary>
    /// Downloads the file produced by a staged export job (range requests supported). The
    /// staged file is removed by the maintenance task after the retention period, and in any
    /// case when the server stops or starts, after which this answers 404.
    /// </summary>
    [HttpGet("{id:guid}/file")]
    [Authorize(Policy = SdsPolicies.Admin)]
    public async Task<IActionResult> File(Guid id, CancellationToken ct)
    {
        var job = await jobs.GetAsync(id, ct);
        if (job.Status != JobStatus.Succeeded || job.Type != "ExportImage" || job.ResultJson is null)
        {
            return Conflict(new ProblemDetails { Title = "No file", Detail = "The job is not a finished export." });
        }

        var result = System.Text.Json.JsonSerializer.Deserialize<ExportResult>(job.ResultJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        if (!result.Staged || !System.IO.File.Exists(result.Path))
        {
            return NotFound();
        }

        return PhysicalFile(result.Path, "application/octet-stream", Path.GetFileName(result.Path), enableRangeProcessing: true);
    }

    /// <summary>Builds a 202 Accepted response pointing to the job.</summary>
    internal static IActionResult Accepted(ControllerBase controller, Job job) =>
        controller.AcceptedAtAction(nameof(Get), "Jobs", new { id = job.Id }, job);
}
