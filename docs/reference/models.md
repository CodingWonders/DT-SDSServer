# Models

Every request and response body of the API. Property names are shown as they appear on the wire (camelCase). `?` marks a nullable field; null fields are omitted from responses. Enum types link to [Enumerations](enums.md).

Sources: `src/Sds.Core/**/*Dtos.cs`, `src/Sds.Core/Settings/ServerSettings.cs`, `src/Sds.Data/Entities/Misc.cs`, `src/Sds.Imaging/WimImageInfo.cs`.

- Images: [ImageGroupDto](#imagegroupdto), [CreateImageGroupRequest](#createimagegrouprequest), [UpdateImageGroupRequest](#updateimagegrouprequest), [ImageDto](#imagedto), [WimImageInfo](#wimimageinfo), [AddImageRequest](#addimagerequest), [UpdateImageRequest](#updateimagerequest), [ExportImageRequest](#exportimagerequest), [CopyImageRequest](#copyimagerequest), [ReplaceImageRequest](#replaceimagerequest), [ExportResult](#exportresult), [CompactResult](#compactresult), [ClientImageDto](#clientimagedto)
- Security: [AclDto](#acldto), [AclEntryDto](#aclentrydto)
- Jobs: [Job](#job)
- Drivers: [DriverPackageDto](#driverpackagedto), [HardwareIdDto](#hardwareiddto), [DriverImportRequest](#driverimportrequest), [DriverImportResult](#driverimportresult), [SkippedInf](#skippedinf), [UpdateDriverRequest](#updatedriverrequest), [DriverGroupDto](#drivergroupdto), [DriverGroupRequest](#drivergrouprequest), [DriverFilterDto](#driverfilterdto), [DriverGroupMembershipRequest](#drivergroupmembershiprequest), [DriverQueryRequest](#driverqueryrequest), [DriverMatchDto](#drivermatchdto)
- Computers: [ComputerRequest](#computerrequest), [ComputerDto](#computerdto), [PendingDeviceDto](#pendingdevicedto), [ApproveRequest](#approverequest), [RejectRequest](#rejectrequest)
- Client: [ClientRegisterRequest](#clientregisterrequest), [ClientDecision](#clientdecision), [ClientConfigurationDto](#clientconfigurationdto), [GenerateNameRequest](#generatenamerequest), [ClientLogRequest](#clientlogrequest), [ClientLogEntry](#clientlogentry)
- Multicast: [MulticastRequest](#multicastrequest), [MulticastDto](#multicastdto), [MulticastClientDto](#multicastclientdto), [MulticastJoinRequest](#multicastjoinrequest)
- Server: [ServerSettings](server-settings.md), [ReconcileReport](#reconcilereport)

---

## Images

### ImageGroupDto

| Field | Type | Description |
|---|---|---|
| `id` | int | Group id |
| `name` | string | Group name (also the folder name) |
| `description` | string? | |
| `imageCount` | int | Number of images |
| `resourceFileBytes` | long | Size of `Res.RWM` |
| `uncPath` | string | UNC path of the group folder |
| `createdUtc` | date | |
| `modifiedUtc` | date | |

### CreateImageGroupRequest

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | yes | A valid Windows file name |
| `description` | string? | | |

### UpdateImageGroupRequest

Null fields are left unchanged.

| Field | Type | Description |
|---|---|---|
| `name` | string? | New name (renames the folder) |
| `description` | string? | |

### ImageDto

| Field | Type | Description |
|---|---|---|
| `id` | int | Image id |
| `groupId` | int | |
| `groupName` | string | |
| `fileName` | string | Metadata WIM file name, for example `install-(2).wim` |
| `name` | string | Image name |
| `description` | string? | |
| `architecture` | string | `x86`, `amd64`, `arm64`... |
| `productName` | string? | From the WIM XML |
| `editionId` | string? | For example `Professional` |
| `installationType` | string? | For example `Client`, `Server` |
| `productType` | string? | For example `WinNT`, `ServerNT` |
| `flags` | string? | WIM `FLAGS` |
| `version` | string? | `major.minor.build.revision` |
| `servicePackLevel` | int | |
| `hal` | string? | |
| `languages` | string[] | |
| `defaultLanguage` | string? | |
| `totalBytes` | long | Uncompressed size |
| `fileCount` | long | |
| `priority` | int | Lower comes first |
| `enabled` | bool | Disabled images are hidden from clients |
| `hasUnattend` | bool | An image unattend is set |
| `status` | [ImageStatus](enums.md#imagestatus) | |
| `statusMessage` | string? | Detail for `Missing` or `Error` |
| `metadataUnc` | string | UNC path of the metadata WIM |
| `resourceUnc` | string | UNC path of `Res.RWM` |
| `unattendUnc` | string? | UNC path of the image unattend, when set |
| `createdUtc` | date | |
| `modifiedUtc` | date | |

### WimImageInfo

One image inside a source WIM, returned by `POST /api/images/inspect`.

| Field | Type | Description |
|---|---|---|
| `index` | int | Index in the source WIM (1 based) |
| `name` | string | |
| `description` | string? | |
| `displayName` | string? | |
| `displayDescription` | string? | |
| `flags` | string? | |
| `architecture` | string | `unknown` when not recorded |
| `productName` | string? | |
| `editionId` | string? | |
| `installationType` | string? | |
| `productType` | string? | |
| `hal` | string? | |
| `languages` | string[] | |
| `defaultLanguage` | string? | |
| `version` | string? | |
| `servicePackLevel` | int | |
| `totalBytes` | long | |
| `directoryCount` | long | |
| `fileCount` | long | |
| `creationTimeUtc` | date? | |
| `lastModificationTimeUtc` | date? | |

### AddImageRequest

| Field | Type | Default | Description |
|---|---|---|---|
| `sourcePath` | string? | | WIM or ESD path on the server (local or UNC). Required unless `uploadId` is used. |
| `indexes` | int[]? | all | Indexes to add |
| `fileName` | string? | source file name | Metadata WIM name; `-(n)` is added on collision |
| `name` | string? | from WIM | Only when adding one image |
| `description` | string? | from WIM | Only when adding one image |
| `priority` | int? | 500000 | |
| `enabled` | bool | true | |
| `unattendXml` | string? | | Image unattend for every added image |

### UpdateImageRequest

Null fields are left unchanged.

| Field | Type | Description |
|---|---|---|
| `name` | string? | Also written into the metadata WIM |
| `description` | string? | Also written into the metadata WIM |
| `enabled` | bool? | |
| `priority` | int? | |

### ExportImageRequest

| Field | Type | Default | Description |
|---|---|---|---|
| `destinationPath` | string? | | WIM path on the server. Null stages the export for download. |
| `append` | bool | false | Append to an existing destination WIM |
| `name` | string? | | Name of the exported image |
| `description` | string? | | Description of the exported image |

### CopyImageRequest

Used by copy and move.

| Field | Type | Required | Description |
|---|---|---|---|
| `targetGroup` | string | yes | Name of the target group |
| `fileName` | string? | | Defaults to the source file name |
| `name` | string? | | Defaults to the source name |
| `description` | string? | | Defaults to the source description |

### ReplaceImageRequest

| Field | Type | Default | Description |
|---|---|---|---|
| `sourcePath` | string? | | WIM path on the server. Required unless `uploadId` is used. |
| `index` | int | 1 | Index in the source WIM |

### ExportResult

Result of an `ExportImage` job.

| Field | Type | Description |
|---|---|---|
| `path` | string | Server path of the exported WIM |
| `bytes` | long | File size |
| `index` | int | Index of the image in that WIM (greater than 1 when appended) |
| `name` | string | Image name |
| `staged` | bool | True when the file is in staging and can be downloaded through `GET /api/jobs/{id}/file` |

### CompactResult

Result of a `CompactGroup` job.

| Field | Type | Description |
|---|---|---|
| `group` | string | |
| `bytesReclaimed` | long | |
| `resourceFileBytes` | long | New size of `Res.RWM` |

### ClientImageDto

Image as seen by a deployment client. `fileName`, `priority` and `modifiedUtc` mirror the `Get-WdsInstallImage` fields WDS based scripts use.

| Field | Type | Description |
|---|---|---|
| `id` | int | |
| `group` | string | |
| `name` | string | |
| `description` | string? | |
| `architecture` | string | |
| `languages` | string[] | |
| `defaultLanguage` | string? | |
| `version` | string? | |
| `totalBytes` | long | |
| `metadataUnc` | string | Metadata WIM to apply (index 1) |
| `resourceUnc` | string | `Res.RWM` to reference |
| `unattendUnc` | string? | Image unattend |
| `fileName` | string | |
| `priority` | int | |
| `modifiedUtc` | date | |

---

## Security

### AclDto

| Field | Type | Default | Description |
|---|---|---|---|
| `inheritFromParent` | bool | true | Inherited entries from the parent folder apply. False makes the explicit entries the whole ACL. |
| `entries` | [AclEntryDto](#aclentrydto)[] | | |

### AclEntryDto

| Field | Type | Default | Description |
|---|---|---|---|
| `identity` | string | | `DOMAIN\name` or a SID. Unresolvable names give `400`. |
| `access` | [AclAccess](enums.md#aclaccess) | `Read` | |
| `allow` | bool | true | False for a deny entry |
| `inherited` | bool | | Read only. Entries with `true` are ignored on PUT. |
| `rights` | string? | | Read only. Raw `FileSystemRights` value. |

---

## Jobs

### Job

| Field | Type | Description |
|---|---|---|
| `id` | guid | |
| `type` | string | `AddImage`, `ExportImage`, `CopyImage`, `MoveImage`, `ReplaceImage`, `CompactGroup`, `ImportDrivers` |
| `description` | string? | Human readable description |
| `status` | [JobStatus](enums.md#jobstatus) | |
| `progress` | int | 0 to 100 |
| `resultJson` | string? | On success: the result serialised as a JSON **string**. See [Jobs](../jobs-and-uploads.md#jobs). |
| `error` | string? | On failure |
| `requestedBy` | string? | Account that queued the job |
| `createdUtc` | date | |
| `startedUtc` | date? | |
| `completedUtc` | date? | |

---

## Drivers

### DriverPackageDto

| Field | Type | Description |
|---|---|---|
| `id` | guid | Package id (the GUID in the folder name) |
| `folderName` | string | `<drvname>_<arch>_{GUID}` |
| `infName` | string | |
| `displayName` | string | Defaults to the INF name without extension |
| `className` | string? | Setup class |
| `classGuid` | guid? | |
| `provider` | string? | |
| `version` | string? | |
| `driverDate` | date? | `yyyy-MM-dd` |
| `architecture` | string | |
| `catalogFile` | string? | |
| `signed` | bool | A catalog file is present |
| `bootCritical` | bool | Storage or network class |
| `enabled` | bool | |
| `sizeBytes` | long | |
| `importedUtc` | date | |
| `hardwareIdCount` | int | |
| `groups` | string[] | Driver group names |
| `uncPath` | string | UNC path of the package folder |
| `hardwareIds` | [HardwareIdDto](#hardwareiddto)[]? | Only from `GET /api/drivers/{id}` |

### HardwareIdDto

| Field | Type |
|---|---|
| `hardwareId` | string |
| `description` | string? |

### DriverImportRequest

| Field | Type | Default | Description |
|---|---|---|---|
| `sourcePath` | string? | | Folder, INF or ZIP on the server. Required unless `uploadId` is used. |
| `architectures` | string[]? | all | Only import these architectures |
| `driverGroups` | string[]? | | Groups to add the packages to (must exist) |
| `enabled` | bool | true | |

### DriverImportResult

Result of an `ImportDrivers` job.

| Field | Type | Description |
|---|---|---|
| `imported` | [DriverPackageDto](#driverpackagedto)[] | New packages |
| `duplicates` | [DriverPackageDto](#driverpackagedto)[] | Packages already in the store (same INF content and architecture) |
| `skipped` | [SkippedInf](#skippedinf)[] | INFs that were not imported |

### SkippedInf

| Field | Type | Description |
|---|---|---|
| `inf` | string | INF path |
| `reason` | string | |

### UpdateDriverRequest

Null fields are left unchanged.

| Field | Type |
|---|---|
| `displayName` | string? |
| `enabled` | bool? |

### DriverGroupDto

| Field | Type | Description |
|---|---|---|
| `id` | int | |
| `name` | string | |
| `description` | string? | |
| `enabled` | bool | |
| `applicability` | [DriverGroupApplicability](enums.md#drivergroupapplicability) | |
| `filters` | [DriverFilterDto](#driverfilterdto)[] | |
| `packageCount` | int | |
| `createdUtc` | date | |

### DriverGroupRequest

| Field | Type | Default | Description |
|---|---|---|---|
| `name` | string | | Required, at most 128 characters |
| `description` | string? | | |
| `enabled` | bool | true | |
| `applicability` | [DriverGroupApplicability](enums.md#drivergroupapplicability) | `Matching` | |
| `filters` | [DriverFilterDto](#driverfilterdto)[] | `[]` | Replaces the existing list |

### DriverFilterDto

| Field | Type | Description |
|---|---|---|
| `type` | [DriverFilterType](enums.md#driverfiltertype) | |
| `policy` | [DriverFilterPolicy](enums.md#driverfilterpolicy) | |
| `values` | string[] | At least one |

### DriverGroupMembershipRequest

| Field | Type |
|---|---|
| `packageIds` | guid[] |

### DriverQueryRequest

| Field | Type | Description |
|---|---|---|
| `architecture` | string | Required. Architecture of the image being installed |
| `manufacturer` | string? | SMBIOS manufacturer |
| `model` | string? | SMBIOS model |
| `chassisType` | string? | |
| `biosVendor` | string? | |
| `biosVersion` | string? | |
| `installImageId` | int? | Image being installed; fills the OS fields when absent |
| `osVersion` | string? | |
| `osEdition` | string? | |
| `osLanguage` | string? | |
| `hardwareIds` | string[] | Hardware and compatible ids of every device |

### DriverMatchDto

| Field | Type | Description |
|---|---|---|
| `packageId` | guid | |
| `displayName` | string | |
| `infName` | string | |
| `architecture` | string | |
| `className` | string? | |
| `bootCritical` | bool | |
| `uncPath` | string | Package folder |
| `infUnc` | string | INF file |
| `matchedHardwareIds` | string[] | Client ids the package matched (upper case). Empty for `All` groups without a match. |
| `groups` | string[] | Groups that selected the package |

---

## Computers

### ComputerRequest

Used by `POST` and `PUT /api/computers`. `PUT` replaces every field.

| Field | Type | Default | Description |
|---|---|---|---|
| `name` | string | | Required. 1 to 15 letters, digits or hyphens; stored upper case |
| `macAddress` | string? | | Any common notation. MAC or GUID required. |
| `smbiosGuid` | string? | | MAC or GUID required |
| `description` | string? | | |
| `architecture` | string? | | |
| `joinDomain` | bool | true | |
| `domain` | string? | | Domain DNS name |
| `organizationalUnit` | string? | | OU distinguished name |
| `user` | string? | | User or group allowed to deploy the device |
| `joinRights` | [JoinRights](enums.md#joinrights) | `JoinOnly` | |
| `referralServer` | string? | | |
| `bootProgram` | string? | | |
| `bootImagePath` | string? | | |
| `clientUnattend` | string? | | Device specific client unattend XML (must be well formed) |
| `imageGroup` | string? | | Restrict the device to this image group (name) |
| `installImageId` | int? | | Preselected image |

### ComputerDto

| Field | Type | Description |
|---|---|---|
| `id` | int | |
| `name` | string | |
| `macAddress` | string? | `AABBCCDDEEFF` |
| `smbiosGuid` | guid? | |
| `description` | string? | |
| `architecture` | string? | |
| `joinDomain` | bool | |
| `domain` | string? | |
| `organizationalUnit` | string? | |
| `user` | string? | |
| `joinRights` | [JoinRights](enums.md#joinrights) | |
| `referralServer` | string? | |
| `bootProgram` | string? | |
| `bootImagePath` | string? | |
| `hasClientUnattend` | bool | Read it with `GET /api/computers/{id}/unattend` |
| `imageGroup` | string? | |
| `installImageId` | int? | |
| `origin` | [ComputerOrigin](enums.md#computerorigin) | |
| `directorySync` | [DirectorySyncState](enums.md#directorysyncstate) | |
| `distinguishedName` | string? | AD object |
| `directoryError` | string? | Last AD error |
| `lastSeenUtc` | date? | Last registration |
| `createdUtc` | date | |
| `modifiedUtc` | date | |

### PendingDeviceDto

| Field | Type | Description |
|---|---|---|
| `requestId` | long | Record id, also the client's `requestId` |
| `macAddress` | string? | |
| `smbiosGuid` | guid? | |
| `architecture` | string? | |
| `ipAddress` | string? | |
| `reportedName` | string? | Name reported by the client |
| `status` | [PendingDeviceStatus](enums.md#pendingdevicestatus) | |
| `firstSeenUtc` | date | |
| `lastSeenUtc` | date | |
| `decidedUtc` | date? | |
| `decidedBy` | string? | Administrator account |
| `message` | string? | Message returned to the client |
| `computerId` | int? | Computer created on approval |

### ApproveRequest

All fields optional; null uses the server default. See [approve](../api/pending-devices.md#post-apipending-devicesidapprove).

| Field | Type |
|---|---|
| `name` | string? |
| `user` | string? |
| `joinRights` | [JoinRights](enums.md#joinrights)? |
| `joinDomain` | bool? |
| `domain` | string? |
| `organizationalUnit` | string? |
| `referralServer` | string? |
| `bootProgram` | string? |
| `bootImagePath` | string? |
| `imageGroup` | string? |
| `message` | string? |

### RejectRequest

| Field | Type |
|---|---|
| `message` | string? |

---

## Client

### ClientRegisterRequest

| Field | Type | Description |
|---|---|---|
| `macAddress` | string? | Boot NIC MAC. MAC or GUID required. |
| `smbiosGuid` | string? | |
| `architecture` | string? | |
| `reportedName` | string? | Name currently reported by the client |

### ClientDecision

| Field | Type | Description |
|---|---|---|
| `status` | [ClientDecisionStatus](enums.md#clientdecisionstatus) | |
| `requestId` | long? | Pending request to poll |
| `pollIntervalSeconds` | int? | Seconds before the next poll (`Pending` only) |
| `message` | string? | Message to display |
| `configuration` | [ClientConfigurationDto](#clientconfigurationdto)? | For `Known` and `Allowed` |

### ClientConfigurationDto

| Field | Type | Description |
|---|---|---|
| `name` | string? | Computer name. Absent for `Allowed`: generate one with `POST /api/client/name`. |
| `joinDomain` | bool | |
| `domain` | string? | |
| `organizationalUnit` | string? | |
| `user` | string? | |
| `joinRights` | [JoinRights](enums.md#joinrights) | |
| `referralServer` | string? | |
| `bootProgram` | string? | |
| `bootImagePath` | string? | |
| `clientUnattend` | string? | Device specific, else server default for the architecture |
| `imageGroup` | string? | Restricted image group |
| `installImageId` | int? | Preselected image |

### GenerateNameRequest

| Field | Type | Description |
|---|---|---|
| `username` | string? | Defaults to the caller. Domain prefix and UPN suffix are removed. |
| `first` | string? | First name (`%First`) |
| `last` | string? | Last name (`%Last`) |
| `macAddress` | string? | (`%MAC`) |

### ClientLogRequest

| Field | Type | Description |
|---|---|---|
| `level` | [ClientLogLevel](enums.md#clientloglevel) | Required, not `None` |
| `message` | string | Required. Truncated at 4000 characters. |
| `macAddress` | string? | |
| `computerName` | string? | |
| `phase` | string? | For example `ImageSelection`, `ApplyImage`, `Finished` |
| `imageName` | string? | |

### ClientLogEntry

| Field | Type | Description |
|---|---|---|
| `id` | long | |
| `timestampUtc` | date | Reception time |
| `level` | [ClientLogLevel](enums.md#clientloglevel) | |
| `macAddress` | string? | Normalised |
| `ipAddress` | string? | |
| `computerName` | string? | |
| `phase` | string? | |
| `imageName` | string? | |
| `message` | string | |

---

## Multicast

### MulticastRequest

| Field | Type | Description |
|---|---|---|
| `name` | string | Required, unique |
| `installImageId` | int | Required. One transmission per image. |
| `type` | [MulticastType](enums.md#multicasttype) | |
| `startClientThreshold` | int? | ScheduledCast: start at this many clients (at least 1) |
| `startTimeUtc` | date? | ScheduledCast: start at this time |

### MulticastDto

| Field | Type | Description |
|---|---|---|
| `id` | int | |
| `name` | string | |
| `installImageId` | int | |
| `imageName` | string? | |
| `type` | [MulticastType](enums.md#multicasttype) | |
| `state` | [MulticastState](enums.md#multicaststate) | |
| `startClientThreshold` | int? | |
| `startTimeUtc` | date? | |
| `createdUtc` | date | |
| `activatedUtc` | date? | |
| `connectedClients` | int | Clients that have not left |
| `clients` | [MulticastClientDto](#multicastclientdto)[] | Every client, including those that left |

### MulticastClientDto

| Field | Type | Description |
|---|---|---|
| `id` | int | `clientId` |
| `macAddress` | string? | |
| `ipAddress` | string? | |
| `computerName` | string? | |
| `percentComplete` | int | |
| `joinedUtc` | date | |
| `leftUtc` | date? | Null while connected |

### MulticastJoinRequest

| Field | Type |
|---|---|
| `macAddress` | string? |
| `computerName` | string? |

---

## Server

### ReconcileReport

Each field is a list of group names, image paths or driver folder names.

| Field | Description |
|---|---|
| `groupsImported` | Groups found on disk and added to the database |
| `groupsRecreated` | Groups in the database whose folder was missing (recreated empty) |
| `imagesImported` | Metadata WIMs found on disk and added |
| `imagesMissing` | Images whose files are missing (marked `Missing`) |
| `imagesRestored` | Images that were `Missing` and are back (marked `Ready`) |
| `driversImported` | Driver folders found on disk and registered |
| `driversMissing` | Packages whose folder is missing |
| `driverFoldersIgnored` | Folders in `DRIVERS` that could not be registered |
| `errors` | Errors encountered |
