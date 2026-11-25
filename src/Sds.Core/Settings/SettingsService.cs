using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sds.Data;
using Sds.Data.Entities;

namespace Sds.Core.Settings;

/// <summary>
/// Loads and saves <see cref="ServerSettings"/>. The settings are cached in memory because
/// every client request reads them; writes go through this service and refresh the cache.
/// </summary>
public sealed class SettingsService(IDbContextFactory<SdsDbContext> dbFactory)
{
    private const string Key = "server";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private volatile ServerSettings? _cache;

    /// <summary>Returns the current settings.</summary>
    public async Task<ServerSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
        _cache = row is null ? new ServerSettings() : JsonSerializer.Deserialize<ServerSettings>(row.Json, Json) ?? new ServerSettings();
        return _cache;
    }

    /// <summary>Validates and stores new settings.</summary>
    public async Task<ServerSettings> SaveAsync(ServerSettings settings, CancellationToken ct = default)
    {
        Validate(settings);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == Key, ct);
        var json = JsonSerializer.Serialize(settings, Json);
        if (row is null)
        {
            db.Settings.Add(new SettingEntry { Key = Key, Json = json });
        }
        else
        {
            row.Json = json;
            row.ModifiedUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        _cache = settings;
        return settings;
    }

    private static void Validate(ServerSettings s)
    {
        if (s.PendingPollIntervalSeconds is < 1 or > 3600)
        {
            throw new SdsValidationException("PendingPollIntervalSeconds must be between 1 and 3600.");
        }

        if (s.PendingMaxRetries < 1)
        {
            throw new SdsValidationException("PendingMaxRetries must be at least 1.");
        }

        if (s.MulticastPortStart > s.MulticastPortEnd || s.MulticastPortStart < 1024 || s.MulticastPortEnd > 65535)
        {
            throw new SdsValidationException("The multicast port range is invalid.");
        }

        if (!System.Net.IPAddress.TryParse(s.MulticastAddressStart, out _) || !System.Net.IPAddress.TryParse(s.MulticastAddressEnd, out _))
        {
            throw new SdsValidationException("The multicast address range is invalid.");
        }

        // Validate the naming policy by expanding it once with sample values.
        Computers.NamingPolicy.Validate(s.NamingPolicy);

        if (s.AccountLocation == AccountLocation.Specific && string.IsNullOrWhiteSpace(s.OrganizationalUnit))
        {
            throw new SdsValidationException("OrganizationalUnit is required when AccountLocation is Specific.");
        }
    }
}
