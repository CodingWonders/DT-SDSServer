using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sds.Core.Images;
using Sds.Core.Settings;
using Sds.Core.Util;
using Sds.Data;
using Sds.Data.Entities;

namespace Sds.Core.Computers;

/// <summary>
/// Decides how the server answers a deployment client, implementing the WDS response policy
/// and the pending device (administrator approval) workflow.
/// </summary>
/// <remarks>
/// Decision table, evaluated for every registration or poll:
/// <list type="number">
///   <item><description>Response policy RespondToNone: every client is ignored.</description></item>
///   <item><description>Known computer (by SMBIOS GUID, then MAC): Known, with its configuration.</description></item>
///   <item><description>Unknown and policy KnownOnly: ignored.</description></item>
///   <item><description>Unknown and policy All without approval: Allowed, with server defaults.</description></item>
///   <item><description>Unknown and policy All with approval: a pending request is created or
///   reused; the client polls until an administrator approves (then Known) or rejects it.</description></item>
/// </list>
/// </remarks>
public sealed class ClientAuthorizationService(
    IDbContextFactory<SdsDbContext> dbFactory,
    ComputerService computers,
    SettingsService settings,
    ILogger<ClientAuthorizationService> logger)
{
    /// <summary>Handles a client registration (first contact or reboot).</summary>
    public async Task<ClientDecision> RegisterAsync(ClientRegisterRequest request, string? ipAddress, CancellationToken ct = default)
    {
        var mac = Identifiers.NormalizeMac(request.MacAddress);
        var guid = Identifiers.NormalizeGuid(request.SmbiosGuid);
        var arch = Identifiers.NormalizeArchitecture(request.Architecture);
        if (mac is null && guid is null)
        {
            throw new SdsValidationException("A MAC address or an SMBIOS GUID is required.");
        }

        var s = await settings.GetAsync(ct);

        // Rule 1: the server is silent.
        if (s.ResponsePolicy == ResponsePolicy.RespondToNone)
        {
            return new ClientDecision { Status = ClientDecisionStatus.Ignored };
        }

        // Rule 2: known computer.
        var known = await computers.FindAsync(mac, guid, ct);
        if (known is not null)
        {
            await computers.TouchAsync(known.Id, arch, ct);
            return await KnownDecisionAsync(known, arch, null, ct);
        }

        // Rule 3: only known clients are answered.
        if (s.ResponsePolicy == ResponsePolicy.KnownOnly)
        {
            logger.LogInformation("Ignoring unknown client {Mac} {Guid} (known clients only)", mac, guid);
            return new ClientDecision { Status = ClientDecisionStatus.Ignored };
        }

        // Rule 4: everyone is welcome.
        if (!s.RequireApprovalForUnknown)
        {
            return new ClientDecision
            {
                Status = ClientDecisionStatus.Allowed,
                Configuration = await DefaultConfigurationAsync(s, arch, ct),
            };
        }

        // Rule 5: approval required.
        return await PendingDecisionAsync(mac, guid, arch, ipAddress, request.ReportedName, s, ct);
    }

    /// <summary>Handles a poll of a pending request.</summary>
    public async Task<ClientDecision> PollAsync(long requestId, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pending = await db.PendingDevices.FirstOrDefaultAsync(p => p.Id == requestId, ct)
            ?? throw new SdsNotFoundException($"Request {requestId} not found or expired.");

        pending.LastSeenUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await DecisionForAsync(pending, s, ct);
    }

    /// <summary>Server wide client unattend for an architecture, or null.</summary>
    public async Task<string?> GetServerClientUnattendAsync(string architecture, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.ClientUnattends.AsNoTracking().FirstOrDefaultAsync(u => u.Architecture == architecture, ct))?.Content;
    }

    /// <summary>Sets (or with null removes) the server wide client unattend for an architecture.</summary>
    public async Task SetServerClientUnattendAsync(string architecture, string? xml, CancellationToken ct = default)
    {
        architecture = Identifiers.NormalizeArchitecture(architecture) ?? throw new SdsValidationException("Architecture is required.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.ClientUnattends.FirstOrDefaultAsync(u => u.Architecture == architecture, ct);
        if (xml is null)
        {
            if (row is not null)
            {
                db.ClientUnattends.Remove(row);
            }
        }
        else
        {
            ImageService.ValidateXml(xml);
            if (row is null)
            {
                db.ClientUnattends.Add(new ClientUnattend { Architecture = architecture, Content = xml });
            }
            else
            {
                row.Content = xml;
                row.ModifiedUtc = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<ClientDecision> PendingDecisionAsync(string? mac, Guid? guid, string? arch, string? ip, string? reportedName, ServerSettings s, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Reuse the latest request of the same device so a reboot does not create a new
        // entry for the administrator (WDS behaves the same way).
        var pending = await db.PendingDevices
            .Where(p => (guid != null && p.SmbiosGuid == guid) || (mac != null && p.MacAddress == mac))
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync(ct);

        if (pending is not null && pending.Status == PendingDeviceStatus.Pending && IsExpired(pending, s))
        {
            // The previous request timed out on the client side; start a fresh one.
            db.PendingDevices.Remove(pending);
            pending = null;
        }

        if (pending is null || pending.Status == PendingDeviceStatus.Approved)
        {
            // An Approved request whose computer was deleted since: ask again.
            pending = new PendingDevice { MacAddress = mac, SmbiosGuid = guid, Architecture = arch, IpAddress = ip, ReportedName = reportedName };
            db.PendingDevices.Add(pending);
            logger.LogInformation("New pending device {Mac} {Guid} from {Ip}", mac, guid, ip);
        }
        else
        {
            pending.LastSeenUtc = DateTime.UtcNow;
            pending.IpAddress = ip ?? pending.IpAddress;
            pending.Architecture = arch ?? pending.Architecture;
        }

        await db.SaveChangesAsync(ct);
        return await DecisionForAsync(pending, s, ct);
    }

    private async Task<ClientDecision> DecisionForAsync(PendingDevice pending, ServerSettings s, CancellationToken ct)
    {
        switch (pending.Status)
        {
            case PendingDeviceStatus.Rejected:
                return new ClientDecision { Status = ClientDecisionStatus.Rejected, RequestId = pending.Id, Message = pending.Message ?? "The administrator rejected this computer." };

            case PendingDeviceStatus.Approved when pending.ComputerId is int computerId:
                await using (var db = await dbFactory.CreateDbContextAsync(ct))
                {
                    var computer = await db.Computers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == computerId, ct);
                    if (computer is not null)
                    {
                        return await KnownDecisionAsync(computer, pending.Architecture, pending, ct);
                    }
                }

                // Computer deleted after approval: treat as still pending.
                goto default;

            default:
                if (IsExpired(pending, s))
                {
                    return new ClientDecision { Status = ClientDecisionStatus.Ignored, RequestId = pending.Id, Message = "The approval request timed out." };
                }

                return new ClientDecision
                {
                    Status = ClientDecisionStatus.Pending,
                    RequestId = pending.Id,
                    PollIntervalSeconds = s.PendingPollIntervalSeconds,
                    Message = s.PendingMessage,
                };
        }
    }

    private async Task<ClientDecision> KnownDecisionAsync(Computer c, string? arch, PendingDevice? pending, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = c.ImageGroupId is int gid ? (await db.ImageGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gid, ct))?.Name : null;
        var effectiveArch = arch ?? c.Architecture;
        var unattend = c.ClientUnattend ?? (effectiveArch is null ? null : await GetServerClientUnattendAsync(effectiveArch, ct));
        var s = await settings.GetAsync(ct);

        return new ClientDecision
        {
            Status = ClientDecisionStatus.Known,
            RequestId = pending?.Id,
            Message = pending?.Message,
            Configuration = new ClientConfigurationDto
            {
                Name = c.Name,
                JoinDomain = c.JoinDomain,
                Domain = c.Domain ?? s.Domain,
                OrganizationalUnit = c.OrganizationalUnit ?? DefaultOu(s),
                User = c.User,
                JoinRights = c.JoinRights,
                ReferralServer = c.ReferralServer,
                BootProgram = c.BootProgram ?? s.DefaultBootProgram,
                BootImagePath = c.BootImagePath,
                ClientUnattend = unattend,
                ImageGroup = group,
                InstallImageId = c.InstallImageId,
            },
        };
    }

    private async Task<ClientConfigurationDto> DefaultConfigurationAsync(ServerSettings s, string? arch, CancellationToken ct) => new()
    {
        Name = null, // Generated at setup time from the user through POST /api/client/name.
        JoinDomain = s.JoinDomain,
        Domain = s.Domain,
        OrganizationalUnit = DefaultOu(s),
        JoinRights = s.DefaultJoinRights,
        User = s.DefaultJoinUser,
        BootProgram = s.DefaultBootProgram,
        ClientUnattend = arch is null ? null : await GetServerClientUnattendAsync(arch, ct),
    };

    /// <summary>
    /// OU for new accounts. Only the Specific mode yields an explicit OU here; the other modes
    /// (server domain, user domain, user OU) are resolved by the directory at join time.
    /// </summary>
    internal static string? DefaultOu(ServerSettings s) => s.AccountLocation == AccountLocation.Specific ? s.OrganizationalUnit : null;

    /// <summary>A pending request expires after poll interval times max retries (WDS semantics).</summary>
    internal static bool IsExpired(PendingDevice p, ServerSettings s) =>
        p.Status == PendingDeviceStatus.Pending &&
        p.FirstSeenUtc.AddSeconds((double)s.PendingPollIntervalSeconds * s.PendingMaxRetries) < DateTime.UtcNow;
}
