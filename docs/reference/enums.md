# Enumerations

Enums are sent and received as strings. Source: `src/Sds.Data/Entities/Enums.cs` unless noted.

## ImageStatus

| Value | Meaning |
|---|---|
| `Importing` | Being added by a background job |
| `Ready` | Ready to be deployed |
| `Missing` | The metadata WIM or `Res.RWM` is missing on disk (found by reconcile) |
| `Error` | The last operation on the image failed |

## JobStatus

| Value | Meaning |
|---|---|
| `Queued` | Waiting to start |
| `Running` | Running |
| `Succeeded` | Finished; `resultJson` is set |
| `Failed` | Finished with an error; `error` is set |

## AclAccess

Source: `src/Sds.Core/Security/FileAclService.cs`.

| Value | NTFS rights |
|---|---|
| `Read` | Read and execute |
| `Modify` | Modify |
| `FullControl` | Full control |

## DriverGroupApplicability

| Value | Meaning |
|---|---|
| `Matching` | Only packages that match the client's hardware ids (WDS default) |
| `All` | Every package of the group |

## DriverFilterType

| Value | Client value compared |
|---|---|
| `Manufacturer` | SMBIOS manufacturer |
| `Model` | SMBIOS model |
| `ChassisType` | Chassis type, for example `Desktop`, `Laptop` |
| `BiosVendor` | BIOS vendor |
| `BiosVersion` | BIOS version |
| `InstallImage` | Image id (values may be ids or image names) |
| `OsVersion` | OS version (dotted prefix match) |
| `OsEdition` | OS edition (`EditionId`) |
| `OsLanguage` | OS language |

## DriverFilterPolicy

| Value | Meaning |
|---|---|
| `Include` | The client value must be one of the values |
| `Exclude` | The client value must be none of the values |

## JoinRights

| Value | Meaning |
|---|---|
| `JoinOnly` | The user may join the computer but not reset an existing account |
| `Full` | The user gets full control of the computer account |

## ComputerOrigin

| Value | Meaning |
|---|---|
| `Prestaged` | Created by an administrator |
| `Approved` | Created when a pending device was approved |
| `ActiveDirectory` | Imported from Active Directory |

## DirectorySyncState

| Value | Meaning |
|---|---|
| `NotApplicable` | AD integration is off |
| `InSync` | The AD object is up to date |
| `Failed` | The last AD write failed; see `directoryError` |

## PendingDeviceStatus

| Value | Meaning |
|---|---|
| `Pending` | Waiting for an administrator |
| `Approved` | Approved; a computer was created |
| `Rejected` | Rejected |

## ClientDecisionStatus

Source: `src/Sds.Core/Computers/ComputerDtos.cs`.

| Value | Meaning |
|---|---|
| `Known` | Known computer; configuration included |
| `Allowed` | Unknown client allowed by policy; default configuration included |
| `Pending` | Waiting for approval; poll again |
| `Rejected` | An administrator rejected the client |
| `Ignored` | The server does not answer this client |

## MulticastType

| Value | Meaning |
|---|---|
| `AutoCast` | Starts when a client joins |
| `ScheduledCast` | Starts at a client threshold or a start time |

## MulticastState

| Value | Meaning |
|---|---|
| `Waiting` | Waiting for its start criteria |
| `Active` | Started |
| `Stopped` | Stopped by an administrator |

## ClientLogLevel

Ordered: a level includes every level below it.

| Value | Number | Meaning |
|---|---|---|
| `None` | 0 | Logging disabled (not valid on an entry) |
| `Errors` | 1 | Errors only |
| `Warnings` | 2 | Errors and warnings |
| `Info` | 3 | Errors, warnings and information |

## ResponsePolicy

Source: `src/Sds.Core/Settings/ServerSettings.cs`.

| Value | Meaning |
|---|---|
| `RespondToNone` | Answer no client |
| `KnownOnly` | Answer known computers only |
| `All` | Answer every client (unknown ones may need approval) |

## AccountLocation

Source: `src/Sds.Core/Settings/ServerSettings.cs`.

| Value | New computer accounts go to |
|---|---|
| `ServerDomain` | The SDS server's domain (default computers container) |
| `UserDomain` | The domain of the user performing the installation |
| `UserOrganizationalUnit` | The OU of the user performing the installation |
| `Specific` | The `organizationalUnit` setting |

## MulticastTransferMode

Source: `src/Sds.Core/Settings/ServerSettings.cs`.

| Value | Meaning |
|---|---|
| `SingleSession` | All clients in one session at the same speed |
| `TwoStreams` | Slow and fast sessions |
| `ThreeStreams` | Slow, medium and fast sessions |
