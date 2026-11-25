using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sds.Core.Images;
using Sds.Core.Settings;
using Sds.Core.Util;
using Sds.Data;
using Sds.Data.Entities;
using Sds.DirectoryServices;

namespace Sds.Core.Computers;

/// <summary>
/// Known computers (WDS prestaged devices). The database is the source of truth; when the
/// Active Directory provider is enabled every change is mirrored to a computer object.
/// </summary>
public sealed class ComputerService(
    IDbContextFactory<SdsDbContext> dbFactory,
    IComputerDirectory directory,
    SettingsService settings,
    ILogger<ComputerService> logger)
{
    /// <summary>Lists computers, optionally filtered by a search string (name, MAC or GUID).</summary>
    public async Task<List<ComputerDto>> ListAsync(string? search, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Computers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var mac = new string(search.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
            query = query.Where(c => EF.Functions.Like(c.Name, $"%{search}%")
                || (mac.Length > 0 && c.MacAddress != null && c.MacAddress.Contains(mac)));
        }

        var list = await query.OrderBy(c => c.Name).ToListAsync(ct);
        var groups = await GroupNamesAsync(db, ct);
        return list.Select(c => ToDto(c, groups)).ToList();
    }

    /// <summary>Returns one computer.</summary>
    public async Task<ComputerDto> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.Computers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new SdsNotFoundException($"Computer {id} not found.");
        return ToDto(c, await GroupNamesAsync(db, ct));
    }

    /// <summary>Returns the device specific client unattend.</summary>
    public async Task<string?> GetClientUnattendAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.Computers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new SdsNotFoundException($"Computer {id} not found.");
        return c.ClientUnattend;
    }

    /// <summary>Prestages a computer.</summary>
    public Task<ComputerDto> CreateAsync(ComputerRequest request, ComputerOrigin origin = ComputerOrigin.Prestaged, CancellationToken ct = default) =>
        SaveAsync(null, request, origin, ct);

    /// <summary>Replaces the properties of a computer.</summary>
    public Task<ComputerDto> UpdateAsync(int id, ComputerRequest request, CancellationToken ct = default) =>
        SaveAsync(id, request, null, ct);

    /// <summary>Deletes a computer, and its AD object when requested.</summary>
    public async Task DeleteAsync(int id, bool deleteFromDirectory, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.Computers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new SdsNotFoundException($"Computer {id} not found.");
        db.Computers.Remove(c);
        await db.SaveChangesAsync(ct);

        if (deleteFromDirectory && directory.IsExternal)
        {
            var result = await directory.DeleteAsync(c.Name, ct);
            if (!result.Success)
            {
                logger.LogWarning("Computer {Name} deleted from SDS but not from AD: {Error}", c.Name, result.Error);
            }
        }
    }

    /// <summary>Re-sends a computer to Active Directory (useful after a failed sync).</summary>
    public async Task<ComputerDto> SyncAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.Computers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new SdsNotFoundException($"Computer {id} not found.");
        await SyncToDirectoryAsync(c, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(c, await GroupNamesAsync(db, ct));
    }

    /// <summary>
    /// Finds a known computer by SMBIOS GUID first, then MAC address (WDS lookup order).
    /// When AD integration is on and the device exists only in AD, it is imported.
    /// </summary>
    public async Task<Computer?> FindAsync(string? mac, Guid? guid, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        Computer? found = null;
        if (guid is not null)
        {
            found = await db.Computers.AsNoTracking().FirstOrDefaultAsync(c => c.SmbiosGuid == guid, ct);
        }

        if (found is null && mac is not null)
        {
            found = await db.Computers.AsNoTracking().FirstOrDefaultAsync(c => c.MacAddress == mac, ct);
        }

        if (found is null && directory.IsExternal)
        {
            var entry = await directory.FindAsync(mac, guid, ct);
            if (entry is not null && !await db.Computers.AnyAsync(c => c.Name == entry.Name, ct))
            {
                found = new Computer
                {
                    Name = entry.Name,
                    MacAddress = mac,
                    SmbiosGuid = guid,
                    Description = entry.Description,
                    ReferralServer = entry.ReferralServer,
                    OrganizationalUnit = entry.OrganizationalUnit,
                    Origin = ComputerOrigin.ActiveDirectory,
                    DirectorySync = DirectorySyncState.InSync,
                };
                db.Computers.Add(found);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Imported computer {Name} from Active Directory", found.Name);
            }
        }

        return found;
    }

    /// <summary>Records the last contact time of a known client.</summary>
    public async Task TouchAsync(int id, string? architecture, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Computers.Where(c => c.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.LastSeenUtc, DateTime.UtcNow)
            .SetProperty(c => c.Architecture, c => architecture ?? c.Architecture), ct);
    }

    /// <summary>True when a name is used in SDS or (with AD on) in Active Directory.</summary>
    public async Task<bool> IsNameTakenAsync(string name, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Computers.AnyAsync(c => c.Name == name, ct))
        {
            return true;
        }

        return directory.IsExternal && await directory.NameExistsAsync(name, ct);
    }

    /// <summary>Generates a computer name with the server naming policy.</summary>
    public async Task<string> GenerateNameAsync(NamingValues values, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        return await NamingPolicy.GenerateAsync(s.NamingPolicy, values, n => IsNameTakenAsync(n, ct));
    }

    private async Task<ComputerDto> SaveAsync(int? id, ComputerRequest request, ComputerOrigin? origin, CancellationToken ct)
    {
        // Validation of every identifier.
        var name = Identifiers.ValidateComputerName(request.Name);
        var mac = Identifiers.NormalizeMac(request.MacAddress);
        var guid = Identifiers.NormalizeGuid(request.SmbiosGuid);
        if (mac is null && guid is null)
        {
            throw new SdsValidationException("A MAC address or an SMBIOS GUID is required to identify the device.");
        }

        if (request.ClientUnattend is not null)
        {
            ImageService.ValidateXml(request.ClientUnattend);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        Computer computer;
        if (id is null)
        {
            computer = new Computer { Name = name, Origin = origin ?? ComputerOrigin.Prestaged };
            db.Computers.Add(computer);
        }
        else
        {
            computer = await db.Computers.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new SdsNotFoundException($"Computer {id} not found.");
        }

        // Uniqueness of name, MAC and GUID across other computers.
        var otherId = computer.Id;
        if (await db.Computers.AnyAsync(c => c.Id != otherId && c.Name == name, ct))
        {
            throw new SdsConflictException($"A computer named '{name}' already exists.");
        }

        if (mac is not null && await db.Computers.AnyAsync(c => c.Id != otherId && c.MacAddress == mac, ct))
        {
            throw new SdsConflictException($"Another computer already uses MAC address {mac}.");
        }

        if (guid is not null && await db.Computers.AnyAsync(c => c.Id != otherId && c.SmbiosGuid == guid, ct))
        {
            throw new SdsConflictException($"Another computer already uses SMBIOS GUID {guid}.");
        }

        int? groupId = null;
        if (request.ImageGroup is not null)
        {
            groupId = (await db.ImageGroups.FirstOrDefaultAsync(g => g.Name == request.ImageGroup, ct)
                ?? throw new SdsValidationException($"Image group '{request.ImageGroup}' not found.")).Id;
        }

        if (request.InstallImageId is not null && !await db.Images.AnyAsync(i => i.Id == request.InstallImageId, ct))
        {
            throw new SdsValidationException($"Image {request.InstallImageId} not found.");
        }

        // Renaming in AD is not supported by WDS either: delete the old object first.
        var oldName = computer.Name;

        computer.Name = name;
        computer.MacAddress = mac;
        computer.SmbiosGuid = guid;
        computer.Description = request.Description;
        computer.Architecture = Identifiers.NormalizeArchitecture(request.Architecture);
        computer.JoinDomain = request.JoinDomain;
        computer.Domain = request.Domain;
        computer.OrganizationalUnit = request.OrganizationalUnit;
        computer.User = request.User;
        computer.JoinRights = request.JoinRights;
        computer.ReferralServer = request.ReferralServer;
        computer.BootProgram = request.BootProgram;
        computer.BootImagePath = request.BootImagePath;
        computer.ClientUnattend = request.ClientUnattend;
        computer.ImageGroupId = groupId;
        computer.InstallImageId = request.InstallImageId;
        computer.ModifiedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        if (directory.IsExternal)
        {
            if (id is not null && !string.Equals(oldName, name, StringComparison.OrdinalIgnoreCase))
            {
                await directory.DeleteAsync(oldName, ct);
            }

            await SyncToDirectoryAsync(computer, ct);
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("{Action} computer {Name}", id is null ? "Created" : "Updated", name);
        return ToDto(computer, await GroupNamesAsync(db, ct));
    }

    private async Task SyncToDirectoryAsync(Computer c, CancellationToken ct)
    {
        if (!directory.IsExternal)
        {
            c.DirectorySync = DirectorySyncState.NotApplicable;
            return;
        }

        var result = await directory.UpsertAsync(new DirectoryComputer
        {
            Name = c.Name,
            MacAddress = c.MacAddress,
            SmbiosGuid = c.SmbiosGuid,
            OrganizationalUnit = c.OrganizationalUnit,
            Description = c.Description,
            ReferralServer = c.ReferralServer,
            BootProgram = c.BootProgram,
            BootImagePath = c.BootImagePath,
            JoinUser = c.User,
            FullJoinRights = c.JoinRights == JoinRights.Full,
        }, ct);

        c.DirectorySync = result.Success ? DirectorySyncState.InSync : DirectorySyncState.Failed;
        c.DirectoryError = result.Error;
        c.DistinguishedName = result.DistinguishedName ?? c.DistinguishedName;
    }

    private static async Task<Dictionary<int, string>> GroupNamesAsync(SdsDbContext db, CancellationToken ct) =>
        await db.ImageGroups.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, ct);

    internal static ComputerDto ToDto(Computer c, IReadOnlyDictionary<int, string> groups) => new()
    {
        Id = c.Id,
        Name = c.Name,
        MacAddress = c.MacAddress,
        SmbiosGuid = c.SmbiosGuid,
        Description = c.Description,
        Architecture = c.Architecture,
        JoinDomain = c.JoinDomain,
        Domain = c.Domain,
        OrganizationalUnit = c.OrganizationalUnit,
        User = c.User,
        JoinRights = c.JoinRights,
        ReferralServer = c.ReferralServer,
        BootProgram = c.BootProgram,
        BootImagePath = c.BootImagePath,
        HasClientUnattend = c.ClientUnattend is not null,
        ImageGroup = c.ImageGroupId is int g && groups.TryGetValue(g, out var gn) ? gn : null,
        InstallImageId = c.InstallImageId,
        Origin = c.Origin,
        DirectorySync = c.DirectorySync,
        DistinguishedName = c.DistinguishedName,
        DirectoryError = c.DirectoryError,
        LastSeenUtc = c.LastSeenUtc,
        CreatedUtc = c.CreatedUtc,
        ModifiedUtc = c.ModifiedUtc,
    };
}
