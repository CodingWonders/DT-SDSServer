namespace Sds.Data.Entities;

/// <summary>Lifecycle status of an install image record.</summary>
public enum ImageStatus
{
    /// <summary>The image is being added by a background job.</summary>
    Importing,

    /// <summary>The image is ready to be deployed.</summary>
    Ready,

    /// <summary>The metadata WIM or Res.RWM is missing on disk (detected by the reconciler).</summary>
    Missing,

    /// <summary>The last operation on the image failed.</summary>
    Error,
}

/// <summary>Driver group applicability, identical to the WDS setting of the same name.</summary>
public enum DriverGroupApplicability
{
    /// <summary>Install only the packages that match the hardware of the client (WDS default).</summary>
    Matching,

    /// <summary>Install every package of the group regardless of the client hardware.</summary>
    All,
}

/// <summary>Filter types available on a driver group (same list as WDS).</summary>
public enum DriverFilterType
{
    /// <summary>Computer manufacturer (SMBIOS).</summary>
    Manufacturer,

    /// <summary>Computer model (SMBIOS).</summary>
    Model,

    /// <summary>Chassis type (SMBIOS), for example Desktop or Laptop.</summary>
    ChassisType,

    /// <summary>BIOS vendor.</summary>
    BiosVendor,

    /// <summary>BIOS version.</summary>
    BiosVersion,

    /// <summary>Unique identifier of an install image (SDS image id).</summary>
    InstallImage,

    /// <summary>Operating system version of the image being installed (major.minor.build).</summary>
    OsVersion,

    /// <summary>Operating system edition (EditionId) of the image being installed.</summary>
    OsEdition,

    /// <summary>Operating system language of the image being installed.</summary>
    OsLanguage,
}

/// <summary>Filter policy: the client must match one of the values, or none of them.</summary>
public enum DriverFilterPolicy
{
    /// <summary>The group applies only when the client value is in the list.</summary>
    Include,

    /// <summary>The group applies only when the client value is not in the list.</summary>
    Exclude,
}

/// <summary>Domain join rights granted to the user who deploys a computer (WDS "Join rights").</summary>
public enum JoinRights
{
    /// <summary>The user may join the computer to the domain but not reset an existing account.</summary>
    JoinOnly,

    /// <summary>The user gets full control of the computer account.</summary>
    Full,
}

/// <summary>How a known computer entered the database.</summary>
public enum ComputerOrigin
{
    /// <summary>Created by an administrator (WDS "prestaged device").</summary>
    Prestaged,

    /// <summary>Created when a pending device was approved.</summary>
    Approved,

    /// <summary>Imported from Active Directory.</summary>
    ActiveDirectory,
}

/// <summary>Synchronisation state of a computer with the optional Active Directory provider.</summary>
public enum DirectorySyncState
{
    /// <summary>Active Directory integration is disabled or not applicable.</summary>
    NotApplicable,

    /// <summary>The AD object is up to date.</summary>
    InSync,

    /// <summary>The last AD write failed; see the error message on the computer.</summary>
    Failed,
}

/// <summary>State of a device waiting for administrator approval (WDS "Pending Devices").</summary>
public enum PendingDeviceStatus
{
    /// <summary>Waiting for an administrator.</summary>
    Pending,

    /// <summary>Approved; a known computer record has been created.</summary>
    Approved,

    /// <summary>Rejected by an administrator.</summary>
    Rejected,
}

/// <summary>Multicast transmission types (same as WDS).</summary>
public enum MulticastType
{
    /// <summary>Starts as soon as a client requests the image and keeps cycling.</summary>
    AutoCast,

    /// <summary>Starts when a client threshold or a start time is reached.</summary>
    ScheduledCast,
}

/// <summary>State of a multicast transmission.</summary>
public enum MulticastState
{
    /// <summary>Created and waiting for its start criteria.</summary>
    Waiting,

    /// <summary>Started (by criteria or manually).</summary>
    Active,

    /// <summary>Stopped by an administrator.</summary>
    Stopped,
}

/// <summary>Client logging levels, identical to the WDS client logging levels.</summary>
public enum ClientLogLevel
{
    /// <summary>Client logging disabled.</summary>
    None = 0,

    /// <summary>Errors only.</summary>
    Errors = 1,

    /// <summary>Errors and warnings.</summary>
    Warnings = 2,

    /// <summary>Errors, warnings and informational messages.</summary>
    Info = 3,
}

/// <summary>Status of a long running background job.</summary>
public enum JobStatus
{
    /// <summary>Queued and not yet started.</summary>
    Queued,

    /// <summary>Running.</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Succeeded,

    /// <summary>Finished with an error.</summary>
    Failed,
}
