using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Sds.Core.Networking;

/// <summary>
/// Windows Firewall helpers shared by the server and the Web Console. This file is also
/// compiled into Sds.Web (linked source), so it only depends on ASP.NET Core and logging.
/// </summary>
internal static class FirewallRules
{
    // INetFwRule constants (icftypes.h).
    private const int ProtocolTcp = 6;
    private const int DirectionInbound = 1;
    private const int ActionAllow = 1;
    private const int AllProfiles = 0x7FFFFFFF;

    /// <summary>
    /// Ensures an inbound TCP allow rule named <paramref name="name"/> exists for the ports this
    /// process listens on from the network. An existing rule is never modified. Failures are
    /// logged, never thrown.
    /// </summary>
    public static void EnsureForListenPorts(IConfiguration configuration, string name, string description, ILogger logger)
    {
        var ports = GetListenPorts(configuration);
        if (ports.Count == 0)
        {
            // Loopback only (or no address configured): nothing to open.
            logger.LogInformation("The server only listens on loopback addresses; no firewall rule is needed");
            return;
        }

        var portList = string.Join(",", ports);
        try
        {
            Ensure(name, description, portList, logger);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            logger.LogWarning(ex, "The firewall rule {Rule} for TCP port(s) {Ports} could not be created. Create it manually (as administrator) so other computers on the network can connect.", name, portList);
        }
    }

    private static void Ensure(string name, string description, string ports, ILogger logger)
    {
        var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
            ?? throw new InvalidOperationException("The Windows Firewall API (HNetCfg.FwPolicy2) is not available.");
        dynamic policy = Activator.CreateInstance(policyType)!;
        dynamic rules = policy.Rules;

        // Local rules have no effect when Group Policy turns off local rule merging (domain
        // joined servers often do): the rule is created but traffic is still blocked.
        int modifyState = policy.LocalPolicyModifyState;
        if (modifyState != 0)
        {
            logger.LogWarning("Windows Firewall reports that local rules may not apply (LocalPolicyModifyState {State}: 1 = Group Policy overrides local rules, 2 = inbound rules are blocked). Allow TCP port(s) {Ports} in the firewall Group Policy.", modifyState, ports);
        }

        foreach (dynamic existing in rules)
        {
            if (!string.Equals((string)existing.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var existingPorts = (string?)existing.LocalPorts ?? "";
            bool enabled = existing.Enabled;
            int action = existing.Action;
            int direction = existing.Direction;
            if (!enabled || action != ActionAllow || direction != DirectionInbound)
            {
                logger.LogWarning("Firewall rule {Rule} already exists but is disabled or is not an inbound allow rule. The rule was not changed; fix or delete it and restart.", name);
            }
            else if (!SamePorts(existingPorts, ports))
            {
                logger.LogWarning("Firewall rule {Rule} already exists with port(s) {Existing}, but this process listens on {Ports}. The rule was not changed; fix or delete it and restart.", name, existingPorts, ports);
            }
            else
            {
                logger.LogInformation("Firewall rule {Rule} already exists for TCP port(s) {Ports}", name, ports);
            }

            return;
        }

        var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule")
            ?? throw new InvalidOperationException("The Windows Firewall API (HNetCfg.FWRule) is not available.");
        dynamic rule = Activator.CreateInstance(ruleType)!;
        rule.Name = name;
        rule.Description = description;
        rule.Grouping = "DISMTools System Deployment Services";
        rule.Protocol = ProtocolTcp;
        rule.LocalPorts = ports;
        rule.Direction = DirectionInbound;
        rule.Action = ActionAllow;
        rule.Profiles = AllProfiles;
        rule.Enabled = true;
        rules.Add(rule);

        logger.LogInformation("Created firewall rule {Rule} for TCP port(s) {Ports}", name, ports);
    }

    /// <summary>
    /// TCP ports the process listens on from outside the machine: every address of
    /// <c>urls</c> (appsettings <c>Urls</c>, <c>--urls</c>, <c>ASPNETCORE_URLS</c>),
    /// <c>http_ports</c>/<c>https_ports</c> and <c>Kestrel:Endpoints:*:Url</c>. Loopback
    /// addresses, named pipes, Unix sockets and dynamic ports (0) are left out.
    /// </summary>
    public static SortedSet<int> GetListenPorts(IConfiguration configuration)
    {
        var addresses = new List<string>();
        addresses.AddRange(Split(configuration["urls"]));
        addresses.AddRange(configuration.GetSection("Kestrel:Endpoints").GetChildren()
            .Select(endpoint => endpoint["Url"])
            .OfType<string>());

        var ports = new SortedSet<int>();
        foreach (var address in addresses)
        {
            BindingAddress binding;
            try
            {
                binding = BindingAddress.Parse(address);
            }
            catch (FormatException)
            {
                continue;
            }

            if (binding.IsNamedPipe || binding.IsUnixPipe || binding.Port <= 0 || IsLoopback(binding.Host))
            {
                continue;
            }

            ports.Add(binding.Port);
        }

        // http_ports and https_ports bind every interface.
        foreach (var port in Split(configuration["http_ports"]).Concat(Split(configuration["https_ports"])))
        {
            if (int.TryParse(port, out var value) && value > 0)
            {
                ports.Add(value);
            }
        }

        return ports;
    }

    private static IEnumerable<string> Split(string? value) =>
        (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (System.Net.IPAddress.TryParse(host.Trim('[', ']'), out var ip) && System.Net.IPAddress.IsLoopback(ip));

    private static bool SamePorts(string existing, string expected)
    {
        static string Normalize(string value) => string.Join(",", value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal));
        return Normalize(existing) == Normalize(expected);
    }
}
