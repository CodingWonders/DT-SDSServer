using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sds.Core.Networking;

/// <summary>
/// Creates the inbound Windows Firewall rule for the API ports when it is missing, the way
/// the WDS role adds its own rules on installation. An existing rule only gets its ports
/// updated, and only when SDS created it, so an administrator can narrow it (profiles, remote
/// addresses) without SDS undoing the change.
/// </summary>
public sealed class FirewallConfigurator(IConfiguration configuration, IOptions<SdsOptions> options, ILogger<FirewallConfigurator> logger)
{
    private readonly SdsOptions _options = options.Value;

    /// <summary>Ensures the rule exists. Failures are logged, never thrown.</summary>
    public void Run()
    {
        if (!_options.CreateFirewallRule)
        {
            logger.LogInformation("Firewall rule creation is turned off (Sds:CreateFirewallRule)");
            return;
        }

        FirewallRules.EnsureForListenPorts(
            configuration,
            _options.FirewallRuleName,
            "Allows deployment clients and management consoles to reach the SDS REST API.",
            logger);
    }

    /// <inheritdoc cref="FirewallRules.GetListenPorts"/>
    internal static SortedSet<int> GetListenPorts(IConfiguration configuration) =>
        FirewallRules.GetListenPorts(configuration);
}
