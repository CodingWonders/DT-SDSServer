using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Sds.Data.Entities;

namespace Sds.Data;

/// <summary>
/// The SDS internal database (SQLite). It plays the role of the several stores WDS keeps
/// (the image XML files, the driver store database, Active Directory device objects and the
/// pending devices list) in a single transactional file.
/// </summary>
/// <remarks>
/// SQLite was chosen over a JSON or XML document store because driver group filtering and
/// hardware id matching are relational queries, image and driver operations must update
/// several records atomically, and the API serves concurrent requests (WAL journal mode).
/// </remarks>
public class SdsDbContext(DbContextOptions<SdsDbContext> options) : DbContext(options)
{
    /// <summary>Install image groups.</summary>
    public DbSet<ImageGroup> ImageGroups => Set<ImageGroup>();

    /// <summary>Install images.</summary>
    public DbSet<InstallImage> Images => Set<InstallImage>();

    /// <summary>Driver packages.</summary>
    public DbSet<DriverPackage> DriverPackages => Set<DriverPackage>();

    /// <summary>Hardware ids of driver packages.</summary>
    public DbSet<DriverHardwareId> DriverHardwareIds => Set<DriverHardwareId>();

    /// <summary>Driver groups.</summary>
    public DbSet<DriverGroup> DriverGroups => Set<DriverGroup>();

    /// <summary>Driver group filters.</summary>
    public DbSet<DriverGroupFilter> DriverGroupFilters => Set<DriverGroupFilter>();

    /// <summary>Known (prestaged or approved) computers.</summary>
    public DbSet<Computer> Computers => Set<Computer>();

    /// <summary>Pending devices awaiting approval.</summary>
    public DbSet<PendingDevice> PendingDevices => Set<PendingDevice>();

    /// <summary>Server wide client unattend files.</summary>
    public DbSet<ClientUnattend> ClientUnattends => Set<ClientUnattend>();

    /// <summary>Multicast transmissions.</summary>
    public DbSet<MulticastTransmission> MulticastTransmissions => Set<MulticastTransmission>();

    /// <summary>Multicast clients.</summary>
    public DbSet<MulticastClient> MulticastClients => Set<MulticastClient>();

    /// <summary>Client log entries.</summary>
    public DbSet<ClientLogEntry> ClientLogs => Set<ClientLogEntry>();

    /// <summary>Background jobs.</summary>
    public DbSet<Job> Jobs => Set<Job>();

    /// <summary>Server settings.</summary>
    public DbSet<SettingEntry> Settings => Set<SettingEntry>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as text so the database stays readable with any SQLite tool.
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder b)
    {
        // Image groups: names are folder names, so uniqueness is case insensitive like NTFS.
        b.Entity<ImageGroup>(e =>
        {
            e.Property(x => x.Name).UseCollation("NOCASE").HasMaxLength(128);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Images).WithOne(x => x.ImageGroup!).HasForeignKey(x => x.ImageGroupId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<InstallImage>(e =>
        {
            e.Property(x => x.FileName).UseCollation("NOCASE").HasMaxLength(255);
            e.HasIndex(x => new { x.ImageGroupId, x.FileName }).IsUnique();
            e.Property(x => x.Languages).HasConversion(JsonListConverter(), JsonListComparer());
        });

        b.Entity<DriverPackage>(e =>
        {
            e.HasIndex(x => x.FolderName).IsUnique();
            e.HasIndex(x => new { x.InfHash, x.Architecture }).IsUnique();
            e.HasMany(x => x.HardwareIds).WithOne().HasForeignKey(x => x.DriverPackageId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Groups).WithMany(x => x.Packages).UsingEntity("DriverGroupPackages");
        });

        // Hardware ids are looked up on every client driver query, hence the index.
        b.Entity<DriverHardwareId>(e =>
        {
            e.Property(x => x.HardwareId).UseCollation("NOCASE");
            e.HasIndex(x => x.HardwareId);
        });

        b.Entity<DriverGroup>(e =>
        {
            e.Property(x => x.Name).UseCollation("NOCASE");
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Filters).WithOne().HasForeignKey(x => x.DriverGroupId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DriverGroupFilter>(e =>
            e.Property(x => x.Values).HasConversion(JsonListConverter(), JsonListComparer()));

        // A computer is unique by name, MAC and SMBIOS GUID (the two ids are optional).
        b.Entity<Computer>(e =>
        {
            e.Property(x => x.Name).UseCollation("NOCASE").HasMaxLength(63);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.MacAddress).IsUnique().HasFilter("MacAddress IS NOT NULL");
            e.HasIndex(x => x.SmbiosGuid).IsUnique().HasFilter("SmbiosGuid IS NOT NULL");
        });

        b.Entity<PendingDevice>(e =>
        {
            e.HasIndex(x => x.MacAddress);
            e.HasIndex(x => x.SmbiosGuid);
            e.HasIndex(x => x.Status);
        });

        b.Entity<ClientUnattend>().HasKey(x => x.Architecture);

        b.Entity<MulticastTransmission>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasOne(x => x.InstallImage).WithMany().HasForeignKey(x => x.InstallImageId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Clients).WithOne().HasForeignKey(x => x.MulticastTransmissionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ClientLogEntry>().HasIndex(x => x.TimestampUtc);
        b.Entity<Job>().HasIndex(x => x.CreatedUtc);
        b.Entity<SettingEntry>().HasKey(x => x.Key);
    }

    /// <summary>Stores a list of strings as a JSON array in a single TEXT column.</summary>
    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<string>, string> JsonListConverter() =>
        new(v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

    /// <summary>Value comparer so EF detects in place changes of the list.</summary>
    private static ValueComparer<List<string>> JsonListComparer() =>
        new((a, c) => a!.SequenceEqual(c!), v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())), v => v.ToList());
}

/// <summary>
/// Used only by the EF Core tools (dotnet ef migrations add) to build the model at design time.
/// </summary>
public sealed class SdsDbContextDesignFactory : IDesignTimeDbContextFactory<SdsDbContext>
{
    /// <inheritdoc />
    public SdsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SdsDbContext>().UseSqlite("Data Source=design.db").Options);
}
