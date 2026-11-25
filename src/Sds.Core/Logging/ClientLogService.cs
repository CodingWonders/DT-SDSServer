using Microsoft.EntityFrameworkCore;
using Sds.Core.Settings;
using Sds.Core.Util;
using Sds.Data;
using Sds.Data.Entities;

namespace Sds.Core.Logging;

/// <summary>Body of POST /api/client/logs.</summary>
public sealed record ClientLogRequest(ClientLogLevel Level, string Message, string? MacAddress, string? ComputerName, string? Phase, string? ImageName);

/// <summary>
/// WDS client logging: deployment clients send progress and errors; entries are kept
/// according to the configured level and retention.
/// </summary>
public sealed class ClientLogService(IDbContextFactory<SdsDbContext> dbFactory, SettingsService settings)
{
    /// <summary>Stores an entry if logging is enabled and the level is within the configured level.</summary>
    /// <returns>True when the entry was stored.</returns>
    public async Task<bool> AddAsync(ClientLogRequest request, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Level == ClientLogLevel.None)
        {
            throw new SdsValidationException("Level and Message are required.");
        }

        var s = await settings.GetAsync(ct);
        if (!s.ClientLoggingEnabled || request.Level > s.ClientLoggingLevel)
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ClientLogs.Add(new ClientLogEntry
        {
            Level = request.Level,
            Message = request.Message.Length > 4000 ? request.Message[..4000] : request.Message,
            MacAddress = Identifiers.NormalizeMac(request.MacAddress),
            ComputerName = request.ComputerName,
            Phase = request.Phase,
            ImageName = request.ImageName,
            IpAddress = ip,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Queries entries, newest first.</summary>
    public async Task<List<ClientLogEntry>> QueryAsync(DateTime? fromUtc, DateTime? toUtc, string? macAddress, string? computerName, ClientLogLevel? maxLevel, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.ClientLogs.AsNoTracking();
        if (fromUtc is not null)
        {
            query = query.Where(l => l.TimestampUtc >= fromUtc);
        }

        if (toUtc is not null)
        {
            query = query.Where(l => l.TimestampUtc <= toUtc);
        }

        var mac = Identifiers.NormalizeMac(macAddress);
        if (mac is not null)
        {
            query = query.Where(l => l.MacAddress == mac);
        }

        if (computerName is not null)
        {
            query = query.Where(l => l.ComputerName == computerName);
        }

        var entries = await query.OrderByDescending(l => l.Id).Take(Math.Clamp(take, 1, 5000)).ToListAsync(ct);

        // Enum stored as text: filter the level in memory.
        return maxLevel is null ? entries : entries.Where(e => e.Level <= maxLevel).ToList();
    }

    /// <summary>Deletes entries older than the retention period.</summary>
    public async Task<int> CleanupAsync(CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var cutoff = DateTime.UtcNow.AddDays(-s.ClientLogRetentionDays);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ClientLogs.Where(l => l.TimestampUtc < cutoff).ExecuteDeleteAsync(ct);
    }
}
