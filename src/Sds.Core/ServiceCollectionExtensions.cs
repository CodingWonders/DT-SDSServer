using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sds.Core.Computers;
using Sds.Core.Drivers;
using Sds.Core.Images;
using Sds.Core.Jobs;
using Sds.Core.Logging;
using Sds.Core.Maintenance;
using Sds.Core.Multicast;
using Sds.Core.Settings;
using Sds.Core.Storage;
using Sds.Data;
using Sds.DirectoryServices;

namespace Sds.Core;

/// <summary>Dependency injection registration of every SDS core service.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers options, database, services and hosted services.</summary>
    public static IServiceCollection AddSdsCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SdsOptions>(configuration.GetSection(SdsOptions.Section));
        services.Configure<ActiveDirectoryOptions>(configuration.GetSection(SdsOptions.Section + ":ActiveDirectory"));
        services.AddSingleton<ShareLayout>();
        services.AddSingleton<ShareBootstrapper>();

        // Database: a factory so singleton services and background jobs can open short lived
        // contexts. The connection string points at %ProgramData%\SDS\sds.db by default.
        services.AddDbContextFactory<SdsDbContext>((sp, o) =>
        {
            var layout = sp.GetRequiredService<ShareLayout>();
            Directory.CreateDirectory(layout.DataDirectory);
            o.UseSqlite($"Data Source={layout.DatabasePath};Cache=Shared;Default Timeout=30");
        });

        // Directory provider: Active Directory when enabled, otherwise local only.
        services.AddSingleton<IComputerDirectory>(sp =>
            sp.GetRequiredService<IOptions<ActiveDirectoryOptions>>().Value.Enabled
                ? ActivatorUtilities.CreateInstance<ActiveDirectoryComputerDirectory>(sp)
                : new LocalComputerDirectory());

        services.AddSingleton<SettingsService>();
        services.AddSingleton<JobService>();
        services.AddSingleton<ImageService>();
        services.AddSingleton<DriverService>();
        services.AddSingleton<DriverGroupService>();
        services.AddSingleton<ComputerService>();
        services.AddSingleton<ClientAuthorizationService>();
        services.AddSingleton<PendingDeviceService>();
        services.AddSingleton<IMulticastSender, NullMulticastSender>();
        services.AddSingleton<MulticastService>();
        services.AddSingleton<ClientLogService>();
        services.AddSingleton<Reconciler>();
        services.AddSingleton<ExportCleaner>();

        // Order matters: StartupService must finish before jobs and maintenance run. Hosted
        // services stop in reverse order, so StartupService also stops last, which is when it
        // clears the temporary exports.
        services.AddHostedService<StartupService>();
        services.AddHostedService<JobRunner>();
        services.AddHostedService<MaintenanceService>();
        return services;
    }
}
