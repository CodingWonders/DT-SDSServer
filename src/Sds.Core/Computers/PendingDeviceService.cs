using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sds.Core.Settings;
using Sds.Data;
using Sds.Data.Entities;

namespace Sds.Core.Computers;

/// <summary>
/// Administrator side of the pending devices workflow: list, approve (with name and join
/// settings), reject, delete, and periodic cleanup.
/// </summary>
public sealed class PendingDeviceService(
    IDbContextFactory<SdsDbContext> dbFactory,
    ComputerService computers,
    SettingsService settings,
    ILogger<PendingDeviceService> logger)
{
    /// <summary>Lists pending device records, optionally by status.</summary>
    public async Task<List<PendingDeviceDto>> ListAsync(PendingDeviceStatus? status, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.PendingDevices.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(p => p.Status == status);
        }

        return (await query.OrderBy(p => p.Id).ToListAsync(ct)).Select(ToDto).ToList();
    }

    /// <summary>Returns one record.</summary>
    public async Task<PendingDeviceDto> GetAsync(long id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return ToDto(await db.PendingDevices.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new SdsNotFoundException($"Pending device {id} not found."));
    }

    /// <summary>
    /// Approves a pending device: creates the known computer (name from the request or from the
    /// naming policy) and marks the request approved so the next client poll gets Known.
    /// </summary>
    public async Task<PendingDeviceDto> ApproveAsync(long id, ApproveRequest request, string? approver, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pending = await db.PendingDevices.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new SdsNotFoundException($"Pending device {id} not found.");
        if (pending.Status != PendingDeviceStatus.Pending)
        {
            throw new SdsConflictException($"Pending device {id} is already {pending.Status}.");
        }

        // Name: explicit, else naming policy with the user the device is approved for.
        var user = request.User ?? s.DefaultJoinUser ?? approver;
        var name = request.Name ?? await computers.GenerateNameAsync(new NamingValues(null, null, user, pending.MacAddress), ct);

        var computer = await computers.CreateAsync(new ComputerRequest
        {
            Name = name,
            MacAddress = pending.MacAddress,
            SmbiosGuid = pending.SmbiosGuid?.ToString(),
            Architecture = pending.Architecture,
            Description = $"Approved by {approver} on {DateTime.UtcNow:u}",
            JoinDomain = request.JoinDomain ?? s.JoinDomain,
            Domain = request.Domain ?? s.Domain,
            OrganizationalUnit = request.OrganizationalUnit ?? ClientAuthorizationService.DefaultOu(s),
            User = request.User ?? s.DefaultJoinUser,
            JoinRights = request.JoinRights ?? s.DefaultJoinRights,
            ReferralServer = request.ReferralServer,
            BootProgram = request.BootProgram,
            BootImagePath = request.BootImagePath,
            ImageGroup = request.ImageGroup,
        }, ComputerOrigin.Approved, ct);

        pending.Status = PendingDeviceStatus.Approved;
        pending.ComputerId = computer.Id;
        pending.DecidedBy = approver;
        pending.DecidedUtc = DateTime.UtcNow;
        pending.Message = request.Message;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Pending device {Id} approved by {User} as {Name}", id, approver, name);
        return ToDto(pending);
    }

    /// <summary>Approves every pending request with the same options (names from the naming policy).</summary>
    public async Task<List<PendingDeviceDto>> ApproveAllAsync(ApproveRequest request, string? approver, CancellationToken ct = default)
    {
        if (request.Name is not null)
        {
            throw new SdsValidationException("Name cannot be set when approving several devices.");
        }

        var results = new List<PendingDeviceDto>();
        foreach (var p in await ListAsync(PendingDeviceStatus.Pending, ct))
        {
            results.Add(await ApproveAsync(p.RequestId, request, approver, ct));
        }

        return results;
    }

    /// <summary>Rejects a pending device.</summary>
    public async Task<PendingDeviceDto> RejectAsync(long id, RejectRequest request, string? approver, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pending = await db.PendingDevices.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new SdsNotFoundException($"Pending device {id} not found.");
        if (pending.Status != PendingDeviceStatus.Pending)
        {
            throw new SdsConflictException($"Pending device {id} is already {pending.Status}.");
        }

        pending.Status = PendingDeviceStatus.Rejected;
        pending.DecidedBy = approver;
        pending.DecidedUtc = DateTime.UtcNow;
        pending.Message = request.Message;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Pending device {Id} rejected by {User}", id, approver);
        return ToDto(pending);
    }

    /// <summary>Deletes one record.</summary>
    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.PendingDevices.Where(p => p.Id == id).ExecuteDeleteAsync(ct) == 0)
        {
            throw new SdsNotFoundException($"Pending device {id} not found.");
        }
    }

    /// <summary>Deletes every record with the given status (all when null).</summary>
    public async Task<int> DeleteAllAsync(PendingDeviceStatus? status, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PendingDevices.Where(p => status == null || p.Status == status).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Periodic cleanup: removes expired pending requests and approved or rejected records
    /// older than the retention period.
    /// </summary>
    public async Task<int> CleanupAsync(CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var decidedCutoff = DateTime.UtcNow.AddDays(-s.PendingRecordRetentionDays);
        var pendingCutoff = DateTime.UtcNow.AddSeconds(-(double)s.PendingPollIntervalSeconds * s.PendingMaxRetries);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var removed = await db.PendingDevices
            .Where(p => (p.Status != PendingDeviceStatus.Pending && p.DecidedUtc < decidedCutoff)
                     || (p.Status == PendingDeviceStatus.Pending && p.LastSeenUtc < pendingCutoff))
            .ExecuteDeleteAsync(ct);
        if (removed > 0)
        {
            logger.LogInformation("Removed {Count} stale pending device record(s)", removed);
        }

        return removed;
    }

    private static PendingDeviceDto ToDto(PendingDevice p) => new(
        p.Id, p.MacAddress, p.SmbiosGuid, p.Architecture, p.IpAddress, p.ReportedName, p.Status,
        p.FirstSeenUtc, p.LastSeenUtc, p.DecidedUtc, p.DecidedBy, p.Message, p.ComputerId);
}
