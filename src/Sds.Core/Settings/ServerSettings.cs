using Sds.Data.Entities;

namespace Sds.Core.Settings;

/// <summary>
/// PXE/client response policy, the "PXE Response" tab of the WDS server properties.
/// </summary>
public enum ResponsePolicy
{
    /// <summary>Do not respond to any client computer.</summary>
    RespondToNone,

    /// <summary>Respond only to known client computers.</summary>
    KnownOnly,

    /// <summary>Respond to all client computers (known and unknown).</summary>
    All,
}

/// <summary>Where new computer accounts are created (WDS "AD DS" tab, "Computer account location").</summary>
public enum AccountLocation
{
    /// <summary>Same domain as the SDS server (default computers container).</summary>
    ServerDomain,

    /// <summary>Same domain as the user performing the installation.</summary>
    UserDomain,

    /// <summary>Same OU as the user performing the installation.</summary>
    UserOrganizationalUnit,

    /// <summary>The OU given in <see cref="ServerSettings.OrganizationalUnit"/>.</summary>
    Specific,
}

/// <summary>Multicast transfer behaviour (WDS "Multicast" tab).</summary>
public enum MulticastTransferMode
{
    /// <summary>Keep all clients in one session at the same speed.</summary>
    SingleSession,

    /// <summary>Separate clients into slow and fast sessions.</summary>
    TwoStreams,

    /// <summary>Separate clients into slow, medium and fast sessions.</summary>
    ThreeStreams,
}

/// <summary>
/// Run time server policy, stored in the database and editable through the API. Covers the
/// properties of the WDS server property sheet that apply to SDS.
/// </summary>
public sealed record ServerSettings
{
    /// <summary>Who gets an answer from the server.</summary>
    public ResponsePolicy ResponsePolicy { get; init; } = ResponsePolicy.KnownOnly;

    /// <summary>When responding to all clients, require administrator approval for unknown ones.</summary>
    public bool RequireApprovalForUnknown { get; init; } = true;

    /// <summary>Seconds between two polls of a pending client (WDS default 10).</summary>
    public int PendingPollIntervalSeconds { get; init; } = 10;

    /// <summary>Number of polls before a pending client gives up (WDS default 2160, i.e. 6 hours).</summary>
    public int PendingMaxRetries { get; init; } = 2160;

    /// <summary>Message displayed to pending clients.</summary>
    public string PendingMessage { get; init; } = "This computer is waiting for administrator approval.";

    /// <summary>Days after which approved or rejected pending records are deleted (WDS default 30).</summary>
    public int PendingRecordRetentionDays { get; init; } = 30;

    /// <summary>Client naming policy (WDS default %61Username%#).</summary>
    public string NamingPolicy { get; init; } = "%61Username%#";

    /// <summary>Computer account location mode.</summary>
    public AccountLocation AccountLocation { get; init; } = AccountLocation.ServerDomain;

    /// <summary>Default domain DNS name for new computers.</summary>
    public string? Domain { get; init; }

    /// <summary>OU used when <see cref="AccountLocation"/> is Specific.</summary>
    public string? OrganizationalUnit { get; init; }

    /// <summary>Join the computer to the domain after installation (WDS "Join the client to a domain").</summary>
    public bool JoinDomain { get; init; } = true;

    /// <summary>Default join rights for approved devices.</summary>
    public JoinRights DefaultJoinRights { get; init; } = JoinRights.JoinOnly;

    /// <summary>Default user granted join rights on approved devices, if any.</summary>
    public string? DefaultJoinUser { get; init; }

    /// <summary>Client logging enabled.</summary>
    public bool ClientLoggingEnabled { get; init; } = true;

    /// <summary>Client logging level.</summary>
    public ClientLogLevel ClientLoggingLevel { get; init; } = ClientLogLevel.Info;

    /// <summary>Days after which client log entries are deleted.</summary>
    public int ClientLogRetentionDays { get; init; } = 30;

    /// <summary>Default boot program returned for known clients without one (informational for SharpPXE).</summary>
    public string? DefaultBootProgram { get; init; }

    /// <summary>Multicast: first IPv4 address of the range.</summary>
    public string MulticastAddressStart { get; init; } = "239.0.0.1";

    /// <summary>Multicast: last IPv4 address of the range.</summary>
    public string MulticastAddressEnd { get; init; } = "239.0.0.254";

    /// <summary>Multicast: first UDP port.</summary>
    public int MulticastPortStart { get; init; } = 64001;

    /// <summary>Multicast: last UDP port.</summary>
    public int MulticastPortEnd { get; init; } = 65000;

    /// <summary>Multicast: transfer mode.</summary>
    public MulticastTransferMode MulticastTransferMode { get; init; } = MulticastTransferMode.SingleSession;

    /// <summary>Multicast: disconnect clients slower than this many KBps (0 disables).</summary>
    public int MulticastSlowClientThresholdKbps { get; init; }

    /// <summary>Multicast: fall back to unicast for disconnected clients.</summary>
    public bool MulticastFallbackToUnicast { get; init; } = true;
}
