using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Sds.Core;
using Sds.Core.Storage;

namespace Sds.Server.Security;

/// <summary>Names of the authorization policies.</summary>
public static class SdsPolicies
{
    /// <summary>Full administration (writes). Members of Administrators or SDS Administrators.</summary>
    public const string Admin = "SdsAdmin";

    /// <summary>Read only administration. Admins plus members of SDS Operators.</summary>
    public const string Reader = "SdsReader";
}

/// <summary>Requirement: the user belongs to one of the groups of a role.</summary>
/// <param name="AdminOnly">True for the admin role, false for the reader role.</param>
public sealed record SdsRoleRequirement(bool AdminOnly) : IAuthorizationRequirement;

/// <summary>
/// Evaluates <see cref="SdsRoleRequirement"/> from the group SID claims of the Windows token
/// (Negotiate authentication adds one groupsid claim per group of the caller).
/// </summary>
public sealed class SdsRoleHandler : AuthorizationHandler<SdsRoleRequirement>
{
    private readonly HashSet<string> _adminSids;
    private readonly HashSet<string> _readerSids;

    /// <summary>Resolves group names to SIDs once at startup.</summary>
    public SdsRoleHandler(IOptions<SdsOptions> options, ILogger<SdsRoleHandler> logger)
    {
        var o = options.Value;
        _adminSids = Resolve([o.AdministratorsGroup, .. o.ExtraAdministratorGroups], logger);

        // BUILTIN\Administrators always administers SDS, like WDS.
        _adminSids.Add(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value);
        _readerSids = [.. _adminSids, .. Resolve([o.OperatorsGroup, .. o.ExtraOperatorGroups], logger)];
    }

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SdsRoleRequirement requirement)
    {
        var allowed = requirement.AdminOnly ? _adminSids : _readerSids;
        var sids = context.User.FindAll(ClaimTypes.GroupSid).Select(c => c.Value)
            .Concat(context.User.FindAll(ClaimTypes.PrimarySid).Select(c => c.Value));
        if (sids.Any(allowed.Contains))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static HashSet<string> Resolve(IEnumerable<string> names, ILogger logger)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            // Accept raw SIDs too, which is handy for domain groups in configuration.
            if (name.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
            {
                set.Add(name);
                continue;
            }

            var sid = ShareBootstrapper.TryResolve(name);
            if (sid is null)
            {
                logger.LogWarning("Security group {Group} could not be resolved and is ignored", name);
            }
            else
            {
                set.Add(sid.Value);
            }
        }

        return set;
    }
}
