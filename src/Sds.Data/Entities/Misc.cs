namespace Sds.Data.Entities;

/// <summary>A multicast transmission definition (management only, no sender).</summary>
public class MulticastTransmission
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Unique friendly name.</summary>
    public required string Name { get; set; }

    /// <summary>Install image transmitted.</summary>
    public int InstallImageId { get; set; }

    /// <summary>Navigation to the image.</summary>
    public InstallImage? InstallImage { get; set; }

    /// <summary>AutoCast or ScheduledCast.</summary>
    public MulticastType Type { get; set; }

    /// <summary>Current state.</summary>
    public MulticastState State { get; set; } = MulticastState.Waiting;

    /// <summary>ScheduledCast: start when this many clients joined.</summary>
    public int? StartClientThreshold { get; set; }

    /// <summary>ScheduledCast: start at this time (UTC).</summary>
    public DateTime? StartTimeUtc { get; set; }

    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the transmission became active (UTC).</summary>
    public DateTime? ActivatedUtc { get; set; }

    /// <summary>Joined clients.</summary>
    public List<MulticastClient> Clients { get; set; } = [];
}

/// <summary>A client joined to a multicast transmission.</summary>
public class MulticastClient
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Owning transmission.</summary>
    public int MulticastTransmissionId { get; set; }

    /// <summary>Client MAC address.</summary>
    public string? MacAddress { get; set; }

    /// <summary>Client IP address.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Client name.</summary>
    public string? ComputerName { get; set; }

    /// <summary>Progress reported by the client, 0 to 100.</summary>
    public int PercentComplete { get; set; }

    /// <summary>Join time (UTC).</summary>
    public DateTime JoinedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Leave time (UTC), null while still connected.</summary>
    public DateTime? LeftUtc { get; set; }
}

/// <summary>An entry sent by a deployment client (WDS client logging).</summary>
public class ClientLogEntry
{
    /// <summary>Primary key.</summary>
    public long Id { get; set; }

    /// <summary>Server reception time (UTC).</summary>
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Severity.</summary>
    public ClientLogLevel Level { get; set; }

    /// <summary>Client MAC address.</summary>
    public string? MacAddress { get; set; }

    /// <summary>Client IP address.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Client computer name.</summary>
    public string? ComputerName { get; set; }

    /// <summary>Deployment phase (for example ImageSelection, ApplyImage, Finished).</summary>
    public string? Phase { get; set; }

    /// <summary>Install image name, when known.</summary>
    public string? ImageName { get; set; }

    /// <summary>Message text.</summary>
    public required string Message { get; set; }
}

/// <summary>A long running operation (image import, export, compaction, driver import).</summary>
public class Job
{
    /// <summary>Job id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Job type, for example AddImage.</summary>
    public required string Type { get; set; }

    /// <summary>Human readable description.</summary>
    public string? Description { get; set; }

    /// <summary>Status.</summary>
    public JobStatus Status { get; set; } = JobStatus.Queued;

    /// <summary>Progress 0 to 100.</summary>
    public int Progress { get; set; }

    /// <summary>Result payload (JSON) on success.</summary>
    public string? ResultJson { get; set; }

    /// <summary>Error message on failure.</summary>
    public string? Error { get; set; }

    /// <summary>User who started the job.</summary>
    public string? RequestedBy { get; set; }

    /// <summary>Creation time (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Start time (UTC).</summary>
    public DateTime? StartedUtc { get; set; }

    /// <summary>Completion time (UTC).</summary>
    public DateTime? CompletedUtc { get; set; }
}

/// <summary>Key/value settings row; values are JSON documents.</summary>
public class SettingEntry
{
    /// <summary>Setting key.</summary>
    public required string Key { get; set; }

    /// <summary>JSON value.</summary>
    public required string Json { get; set; }

    /// <summary>Last change time (UTC).</summary>
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
}
