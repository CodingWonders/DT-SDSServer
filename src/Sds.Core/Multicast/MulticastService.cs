using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sds.Data;
using Sds.Data.Entities;

namespace Sds.Core.Multicast;

/// <summary>
/// Extension point for an actual multicast sender. SDS only manages transmissions; a sender
/// implementation can be registered later to push data on the wire when a transmission
/// becomes active.
/// </summary>
public interface IMulticastSender
{
    /// <summary>Called when a transmission becomes active.</summary>
    Task StartAsync(MulticastTransmission transmission, CancellationToken ct);

    /// <summary>Called when a transmission is stopped or deleted.</summary>
    Task StopAsync(MulticastTransmission transmission, CancellationToken ct);
}

/// <summary>Default sender: logs state changes only.</summary>
public sealed class NullMulticastSender(ILogger<NullMulticastSender> logger) : IMulticastSender
{
    /// <inheritdoc />
    public Task StartAsync(MulticastTransmission transmission, CancellationToken ct)
    {
        logger.LogInformation("Multicast transmission {Name} is active (no sender configured)", transmission.Name);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(MulticastTransmission transmission, CancellationToken ct)
    {
        logger.LogInformation("Multicast transmission {Name} stopped", transmission.Name);
        return Task.CompletedTask;
    }
}

/// <summary>Body of POST /api/multicast.</summary>
public sealed record MulticastRequest(string Name, int InstallImageId, MulticastType Type, int? StartClientThreshold, DateTime? StartTimeUtc);

/// <summary>Multicast client as returned by the API.</summary>
public sealed record MulticastClientDto(int Id, string? MacAddress, string? IpAddress, string? ComputerName, int PercentComplete, DateTime JoinedUtc, DateTime? LeftUtc);

/// <summary>Multicast transmission as returned by the API.</summary>
public sealed record MulticastDto(
    int Id,
    string Name,
    int InstallImageId,
    string? ImageName,
    MulticastType Type,
    MulticastState State,
    int? StartClientThreshold,
    DateTime? StartTimeUtc,
    DateTime CreatedUtc,
    DateTime? ActivatedUtc,
    int ConnectedClients,
    IReadOnlyList<MulticastClientDto> Clients);

/// <summary>Body of the client join endpoint.</summary>
public sealed record MulticastJoinRequest(string? MacAddress, string? ComputerName);

/// <summary>
/// Multicast transmission management (WDS "Multicast Transmissions"): AutoCast and
/// ScheduledCast definitions, start criteria, client tracking and manual start/stop.
/// </summary>
public sealed class MulticastService(IDbContextFactory<SdsDbContext> dbFactory, IMulticastSender sender, ILogger<MulticastService> logger)
{
    /// <summary>Lists transmissions.</summary>
    public async Task<List<MulticastDto>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.MulticastTransmissions.AsNoTracking().Include(t => t.Clients).Include(t => t.InstallImage).AsSplitQuery().OrderBy(t => t.Name).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    /// <summary>Returns one transmission.</summary>
    public async Task<MulticastDto> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return ToDto(await LoadAsync(db, id, ct));
    }

    /// <summary>Creates a transmission. Only one transmission per image is allowed (as in WDS).</summary>
    public async Task<MulticastDto> CreateAsync(MulticastRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new SdsValidationException("Name is required.");
        }

        if (request.Type == MulticastType.ScheduledCast && request.StartClientThreshold is null && request.StartTimeUtc is null)
        {
            throw new SdsValidationException("A ScheduledCast needs a client threshold, a start time, or both.");
        }

        if (request.StartClientThreshold is < 1)
        {
            throw new SdsValidationException("StartClientThreshold must be at least 1.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Images.AnyAsync(i => i.Id == request.InstallImageId, ct))
        {
            throw new SdsValidationException($"Image {request.InstallImageId} not found.");
        }

        if (await db.MulticastTransmissions.AnyAsync(t => t.Name == request.Name || t.InstallImageId == request.InstallImageId, ct))
        {
            throw new SdsConflictException("A transmission with this name or for this image already exists.");
        }

        var t = new MulticastTransmission
        {
            Name = request.Name.Trim(),
            InstallImageId = request.InstallImageId,
            Type = request.Type,
            StartClientThreshold = request.Type == MulticastType.ScheduledCast ? request.StartClientThreshold : null,
            StartTimeUtc = request.Type == MulticastType.ScheduledCast ? request.StartTimeUtc : null,
        };
        db.MulticastTransmissions.Add(t);
        await db.SaveChangesAsync(ct);
        return await GetAsync(t.Id, ct);
    }

    /// <summary>Starts a transmission now (WDS "Start" on a ScheduledCast).</summary>
    public async Task<MulticastDto> StartAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await LoadAsync(db, id, ct);
        await ActivateAsync(db, t, ct);
        return ToDto(t);
    }

    /// <summary>Stops a transmission and disconnects its clients.</summary>
    public async Task<MulticastDto> StopAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await LoadAsync(db, id, ct);
        if (t.State == MulticastState.Active)
        {
            await sender.StopAsync(t, ct);
        }

        t.State = MulticastState.Stopped;
        foreach (var c in t.Clients.Where(c => c.LeftUtc is null))
        {
            c.LeftUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    /// <summary>Deletes a transmission.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await LoadAsync(db, id, ct);
        if (t.State == MulticastState.Active)
        {
            await sender.StopAsync(t, ct);
        }

        db.MulticastTransmissions.Remove(t);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Disconnects one client.</summary>
    public async Task DisconnectClientAsync(int id, int clientId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var client = await db.MulticastClients.FirstOrDefaultAsync(c => c.Id == clientId && c.MulticastTransmissionId == id, ct)
            ?? throw new SdsNotFoundException($"Client {clientId} not found.");
        client.LeftUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A client joins the transmission of an image. AutoCast becomes active on the first join;
    /// ScheduledCast is checked against its client threshold.
    /// </summary>
    public async Task<MulticastDto> JoinAsync(int installImageId, MulticastJoinRequest request, string? ip, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var t = await db.MulticastTransmissions.Include(x => x.Clients).Include(x => x.InstallImage).FirstOrDefaultAsync(x => x.InstallImageId == installImageId, ct)
            ?? throw new SdsNotFoundException($"No multicast transmission for image {installImageId}.");
        if (t.State == MulticastState.Stopped)
        {
            throw new SdsConflictException("The transmission is stopped.");
        }

        var mac = Util.Identifiers.NormalizeMac(request.MacAddress);
        var client = t.Clients.FirstOrDefault(c => c.LeftUtc is null && mac is not null && c.MacAddress == mac);
        if (client is null)
        {
            client = new MulticastClient { MacAddress = mac, IpAddress = ip, ComputerName = request.ComputerName };
            t.Clients.Add(client);
        }

        await db.SaveChangesAsync(ct);
        await EvaluateAsync(db, t, ct);
        return ToDto(t);
    }

    /// <summary>A client reports progress, or leaves when <paramref name="leave"/> is true.</summary>
    public async Task ReportAsync(int id, int clientId, int percent, bool leave, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var client = await db.MulticastClients.FirstOrDefaultAsync(c => c.Id == clientId && c.MulticastTransmissionId == id, ct)
            ?? throw new SdsNotFoundException($"Client {clientId} not found.");
        client.PercentComplete = Math.Clamp(percent, 0, 100);
        if (leave)
        {
            client.LeftUtc ??= DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Checks the start criteria of every waiting transmission (called periodically).</summary>
    public async Task EvaluateAllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var waiting = await db.MulticastTransmissions.Include(t => t.Clients).Include(t => t.InstallImage).Where(t => t.State == MulticastState.Waiting).ToListAsync(ct);
        foreach (var t in waiting)
        {
            await EvaluateAsync(db, t, ct);
        }
    }

    private async Task EvaluateAsync(SdsDbContext db, MulticastTransmission t, CancellationToken ct)
    {
        if (t.State != MulticastState.Waiting)
        {
            return;
        }

        var connected = t.Clients.Count(c => c.LeftUtc is null);
        var start = t.Type switch
        {
            MulticastType.AutoCast => connected > 0,
            _ => (t.StartClientThreshold is int n && connected >= n) || (t.StartTimeUtc is DateTime when && when <= DateTime.UtcNow && connected > 0),
        };

        if (start)
        {
            await ActivateAsync(db, t, ct);
        }
    }

    private async Task ActivateAsync(SdsDbContext db, MulticastTransmission t, CancellationToken ct)
    {
        if (t.State == MulticastState.Active)
        {
            return;
        }

        t.State = MulticastState.Active;
        t.ActivatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Multicast transmission {Name} activated", t.Name);
        await sender.StartAsync(t, ct);
    }

    private static async Task<MulticastTransmission> LoadAsync(SdsDbContext db, int id, CancellationToken ct) =>
        await db.MulticastTransmissions.Include(t => t.Clients).Include(t => t.InstallImage).AsSplitQuery().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new SdsNotFoundException($"Multicast transmission {id} not found.");

    private static MulticastDto ToDto(MulticastTransmission t) => new(
        t.Id, t.Name, t.InstallImageId, t.InstallImage?.Name, t.Type, t.State, t.StartClientThreshold, t.StartTimeUtc, t.CreatedUtc, t.ActivatedUtc,
        t.Clients.Count(c => c.LeftUtc is null),
        t.Clients.Select(c => new MulticastClientDto(c.Id, c.MacAddress, c.IpAddress, c.ComputerName, c.PercentComplete, c.JoinedUtc, c.LeftUtc)).ToList());
}
