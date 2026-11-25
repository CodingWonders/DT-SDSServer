using Microsoft.EntityFrameworkCore;
using Sds.Core.Storage;
using Sds.Core.Util;
using Sds.Data;
using Sds.Data.Entities;

namespace Sds.Core.Drivers;

/// <summary>
/// Driver groups (WDS "Driver Groups"): named sets of packages with optional hardware and
/// install image filters, plus the evaluation used by clients to select drivers.
/// </summary>
public sealed class DriverGroupService(IDbContextFactory<SdsDbContext> dbFactory, DriverService drivers, ShareLayout layout)
{
    /// <summary>Lists groups.</summary>
    public async Task<List<DriverGroupDto>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var groups = await db.DriverGroups.AsNoTracking().Include(g => g.Filters).Include(g => g.Packages).AsSplitQuery().OrderBy(g => g.Name).ToListAsync(ct);
        return groups.Select(ToDto).ToList();
    }

    /// <summary>Returns one group.</summary>
    public async Task<DriverGroupDto> GetAsync(string name, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return ToDto(await LoadAsync(db, name, ct));
    }

    /// <summary>Creates a group.</summary>
    public async Task<DriverGroupDto> CreateAsync(DriverGroupRequest request, CancellationToken ct = default)
    {
        var name = ValidateName(request.Name);
        ValidateFilters(request.Filters);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.DriverGroups.AnyAsync(g => g.Name == name, ct))
        {
            throw new SdsConflictException($"Driver group '{name}' already exists.");
        }

        var group = new DriverGroup { Name = name };
        Apply(group, request);
        db.DriverGroups.Add(group);
        await db.SaveChangesAsync(ct);
        return ToDto(group);
    }

    /// <summary>Replaces the properties and filters of a group.</summary>
    public async Task<DriverGroupDto> UpdateAsync(string name, DriverGroupRequest request, CancellationToken ct = default)
    {
        var newName = ValidateName(request.Name);
        ValidateFilters(request.Filters);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await LoadAsync(db, name, ct);
        if (!string.Equals(newName, group.Name, StringComparison.OrdinalIgnoreCase) && await db.DriverGroups.AnyAsync(g => g.Name == newName, ct))
        {
            throw new SdsConflictException($"Driver group '{newName}' already exists.");
        }

        group.Name = newName;
        db.DriverGroupFilters.RemoveRange(group.Filters);
        group.Filters.Clear();
        Apply(group, request);
        await db.SaveChangesAsync(ct);
        return ToDto(group);
    }

    /// <summary>Deletes a group (packages stay in the store).</summary>
    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.DriverGroups.Remove(await LoadAsync(db, name, ct));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Lists the packages of a group.</summary>
    public async Task<List<DriverPackageDto>> ListPackagesAsync(string name, CancellationToken ct = default) =>
        await drivers.ListAsync(null, null, (await GetAsync(name, ct)).Name, null, null, ct);

    /// <summary>Adds packages to a group.</summary>
    public async Task<DriverGroupDto> AddPackagesAsync(string name, DriverGroupMembershipRequest request, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await LoadAsync(db, name, ct);
        foreach (var id in request.PackageIds.Distinct())
        {
            if (group.Packages.Any(p => p.Id == id))
            {
                continue;
            }

            group.Packages.Add(await db.DriverPackages.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new SdsValidationException($"Driver package {id} not found."));
        }

        await db.SaveChangesAsync(ct);
        return ToDto(group);
    }

    /// <summary>Removes packages from a group.</summary>
    public async Task<DriverGroupDto> RemovePackagesAsync(string name, DriverGroupMembershipRequest request, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await LoadAsync(db, name, ct);
        group.Packages.RemoveAll(p => request.PackageIds.Contains(p.Id));
        await db.SaveChangesAsync(ct);
        return ToDto(group);
    }

    /// <summary>
    /// Selects the driver packages for a client, the way WDS evaluates driver groups during
    /// the Windows PE phase of a deployment:
    /// </summary>
    /// <remarks>
    /// <list type="number">
    ///   <item><description>Every enabled group whose filters are all satisfied applies.</description></item>
    ///   <item><description>From an applicable group, only enabled packages of the client
    ///   architecture are considered.</description></item>
    ///   <item><description>Applicability Matching keeps packages that declare one of the
    ///   client's hardware or compatible ids; All keeps every package.</description></item>
    /// </list>
    /// </remarks>
    public async Task<List<DriverMatchDto>> QueryAsync(DriverQueryRequest request, CancellationToken ct = default)
    {
        var arch = Identifiers.NormalizeArchitecture(request.Architecture) ?? throw new SdsValidationException("Architecture is required.");
        var clientIds = request.HardwareIds.Select(h => h.Trim().ToUpperInvariant()).Where(h => h.Length > 0).ToHashSet();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // OS attributes default to the ones of the selected install image.
        string? osVersion = request.OsVersion, osEdition = request.OsEdition, osLanguage = request.OsLanguage, imageName = null;
        if (request.InstallImageId is int imageId)
        {
            var image = await db.Images.AsNoTracking().FirstOrDefaultAsync(i => i.Id == imageId, ct) ?? throw new SdsValidationException($"Image {imageId} not found.");
            osVersion ??= image.Version;
            osEdition ??= image.EditionId;
            osLanguage ??= image.DefaultLanguage;
            imageName = image.Name;
        }

        var groups = await db.DriverGroups.AsNoTracking()
            .Where(g => g.Enabled)
            .Include(g => g.Filters)
            .Include(g => g.Packages).ThenInclude(p => p.HardwareIds)
            .AsSplitQuery()
            .ToListAsync(ct);

        var matches = new Dictionary<Guid, (DriverPackage Package, HashSet<string> Ids, List<string> Groups)>();
        foreach (var group in groups)
        {
            var applies = group.Filters.All(f => FilterMatches(f, f.Type switch
            {
                DriverFilterType.Manufacturer => request.Manufacturer,
                DriverFilterType.Model => request.Model,
                DriverFilterType.ChassisType => request.ChassisType,
                DriverFilterType.BiosVendor => request.BiosVendor,
                DriverFilterType.BiosVersion => request.BiosVersion,
                DriverFilterType.OsVersion => osVersion,
                DriverFilterType.OsEdition => osEdition,
                DriverFilterType.OsLanguage => osLanguage,
                DriverFilterType.InstallImage => request.InstallImageId?.ToString(),
                _ => null,
            }, imageName));

            if (!applies)
            {
                continue;
            }

            foreach (var package in group.Packages.Where(p => p.Enabled && p.Architecture == arch))
            {
                var matched = package.HardwareIds.Select(h => h.HardwareId.ToUpperInvariant()).Where(clientIds.Contains).ToList();
                if (group.Applicability == DriverGroupApplicability.Matching && matched.Count == 0)
                {
                    continue;
                }

                if (!matches.TryGetValue(package.Id, out var entry))
                {
                    entry = (package, [], []);
                    matches[package.Id] = entry;
                }

                entry.Ids.UnionWith(matched);
                entry.Groups.Add(group.Name);
            }
        }

        // Boot critical packages first (they are needed by Windows PE), then by name.
        return matches.Values
            .OrderByDescending(m => m.Package.BootCritical).ThenBy(m => m.Package.DisplayName)
            .Select(m => new DriverMatchDto(
                m.Package.Id, m.Package.DisplayName, m.Package.InfName, m.Package.Architecture, m.Package.ClassName, m.Package.BootCritical,
                layout.ToUnc(layout.DriverDirectory(m.Package.FolderName)),
                layout.ToUnc(Path.Combine(layout.DriverDirectory(m.Package.FolderName), m.Package.InfName)),
                m.Ids.OrderBy(x => x).ToList(), m.Groups))
            .ToList();
    }

    /// <summary>
    /// Evaluates one filter. Include: the client value must equal one of the values. Exclude:
    /// it must equal none of them. OS version values match as dotted prefixes ("10.0" matches
    /// "10.0.22621.1"). Install image values may be ids or image names. A missing client value
    /// fails an Include filter and passes an Exclude filter.
    /// </summary>
    internal static bool FilterMatches(DriverGroupFilter filter, string? value, string? imageName)
    {
        bool hit;
        if (filter.Type == DriverFilterType.OsVersion)
        {
            hit = value is not null && filter.Values.Any(v => value == v || value.StartsWith(v.TrimEnd('.') + ".", StringComparison.Ordinal));
        }
        else if (filter.Type == DriverFilterType.InstallImage)
        {
            hit = filter.Values.Any(v => (value is not null && string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
                || (imageName is not null && string.Equals(v, imageName, StringComparison.OrdinalIgnoreCase)));
        }
        else
        {
            hit = value is not null && filter.Values.Any(v => string.Equals(v.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return filter.Policy == DriverFilterPolicy.Include ? hit : !hit;
    }

    private static async Task<DriverGroup> LoadAsync(SdsDbContext db, string name, CancellationToken ct) =>
        await db.DriverGroups.Include(g => g.Filters).Include(g => g.Packages).AsSplitQuery().FirstOrDefaultAsync(g => g.Name == name, ct)
            ?? throw new SdsNotFoundException($"Driver group '{name}' not found.");

    private static void Apply(DriverGroup group, DriverGroupRequest request)
    {
        group.Description = request.Description;
        group.Enabled = request.Enabled;
        group.Applicability = request.Applicability;
        group.Filters.AddRange(request.Filters.Select(f => new DriverGroupFilter
        {
            Type = f.Type,
            Policy = f.Policy,
            Values = f.Values.Select(v => v.Trim()).Where(v => v.Length > 0).ToList(),
        }));
    }

    private static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 128)
        {
            throw new SdsValidationException("Driver group name is required (at most 128 characters).");
        }

        return name.Trim();
    }

    private static void ValidateFilters(List<DriverFilterDto> filters)
    {
        foreach (var f in filters)
        {
            if (f.Values is null || f.Values.Count == 0)
            {
                throw new SdsValidationException($"Filter {f.Type} needs at least one value.");
            }
        }
    }

    private static DriverGroupDto ToDto(DriverGroup g) => new(
        g.Id, g.Name, g.Description, g.Enabled, g.Applicability,
        g.Filters.Select(f => new DriverFilterDto(f.Type, f.Policy, f.Values)).ToList(),
        g.Packages.Count, g.CreatedUtc);
}
