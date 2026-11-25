# Deployment client

`/api/client`: endpoints called by deployment clients, the SDS counterpart of the WDS client protocol. Source: `src/Sds.Server/Controllers/ClientController.cs`.

Read [Deployment client workflow](../client-workflow.md) first for the order of the calls.

The client's IP address is taken from the connection and recorded on registrations, logs and multicast joins.

## POST /api/client/register

Registers a booting client and returns the server's decision. **Anonymous**.

Request body: [ClientRegisterRequest](../reference/models.md#clientregisterrequest)

```json
{
  "macAddress": "00:15:5D:01:02:03",
  "smbiosGuid": "4C4C4544-0042-3510-8051-B4C04F4E4C31",
  "architecture": "x64",
  "reportedName": "MININT-8K2J4F"
}
```

A MAC address or an SMBIOS GUID is required (`400` otherwise). All zero and all `F` GUIDs count as missing.

Response `200`: [ClientDecision](../reference/models.md#clientdecision). The decision rules are in the [workflow](../client-workflow.md#1-register).

Known client:

```json
{
  "status": "Known",
  "configuration": {
    "name": "LAB-PC-014",
    "joinDomain": true,
    "domain": "contoso.com",
    "organizationalUnit": "OU=Lab,DC=contoso,DC=com",
    "user": "CONTOSO\\labtech",
    "joinRights": "JoinOnly",
    "clientUnattend": "<?xml version=\"1.0\"?><unattend ...>",
    "imageGroup": "Windows 11",
    "installImageId": 12
  }
}
```

Pending client:

```json
{
  "status": "Pending",
  "requestId": 381,
  "pollIntervalSeconds": 10,
  "message": "This computer is waiting for administrator approval."
}
```

## GET /api/client/requests/{id}

Polls a pending approval request. **Anonymous**. `id` is the `requestId` from the registration.

Response `200`: [ClientDecision](../reference/models.md#clientdecision): `Pending`, `Known` (approved), `Rejected`, or `Ignored` (timed out). `404` when the request no longer exists; register again.

## GET /api/client/unattend/{architecture}

Returns the WDS client unattend for this device. **Anonymous**.

| Parameter | In | Description |
|---|---|---|
| `architecture` | path | Client architecture |
| `mac` | query | Optional. Client MAC address |
| `guid` | query | Optional. Client SMBIOS GUID |

If `mac` or `guid` identifies a known computer with a device specific client unattend, that one is returned; otherwise the server wide one for the architecture.

Response `200` `application/xml`. `404` when there is none. `400` for an unknown architecture or a malformed MAC or GUID.

## GET /api/client/images

Images the caller may install. **User** (the deploying user's Windows credentials).

| Parameter | In | Description |
|---|---|---|
| `architecture` | query | Optional. Only images of this architecture |
| `group` | query | Optional. Only images of this group (use `configuration.imageGroup`) |

Only images that are enabled, `Ready`, and readable by the caller according to the metadata WIM's NTFS permissions (the WDS user filter) are returned, sorted by priority then name.

Response `200`: array of [ClientImageDto](../reference/models.md#clientimagedto).

```json
[
  {
    "id": 12,
    "group": "Windows 11",
    "name": "Windows 11 Pro",
    "architecture": "amd64",
    "languages": ["en-US"],
    "defaultLanguage": "en-US",
    "version": "10.0.26100.1742",
    "totalBytes": 19212310231,
    "metadataUnc": "\\\\SDS01.contoso.com\\images\\Windows 11\\install.wim",
    "resourceUnc": "\\\\SDS01.contoso.com\\images\\Windows 11\\Res.RWM",
    "fileName": "install.wim",
    "priority": 100,
    "modifiedUtc": "2026-11-24T16:03:10Z"
  }
]
```

## POST /api/client/name

Generates a computer name with the server's [naming policy](../reference/naming-policy.md). Used by clients that were `Allowed` without a name. **User**.

Request body: [GenerateNameRequest](../reference/models.md#generatenamerequest)

```json
{ "username": null, "first": "Ada", "last": "Lovelace", "macAddress": "00155D010203" }
```

`username` defaults to the caller's account name. Which fields matter depends on the policy: the default `%61Username%#` only uses the user name. The name is checked for uniqueness against SDS and, with AD integration, Active Directory.

Response `200`, for the caller `CONTOSO\ada.lovelace` and the default policy:

```json
{ "name": "ADALOVELACE1" }
```

`400` when the policy yields an empty name (no user or MAC information). `409` when no free name can be generated (for example the policy has no `%#` counter and the name is taken).

## POST /api/client/drivers/query

Returns the driver packages to inject for this client's hardware and chosen image. **User**.

Request body: [DriverQueryRequest](../reference/models.md#driverqueryrequest)

```json
{
  "architecture": "amd64",
  "manufacturer": "Dell Inc.",
  "model": "Latitude 7440",
  "chassisType": "Laptop",
  "biosVendor": "Dell Inc.",
  "biosVersion": "1.12.0",
  "installImageId": 12,
  "hardwareIds": [
    "PCI\\VEN_8086&DEV_550A&SUBSYS_0C051028&REV_20",
    "PCI\\VEN_8086&DEV_550A",
    "PCI\\CC_020000"
  ]
}
```

Send both hardware ids and compatible ids of every device. `osVersion`, `osEdition` and `osLanguage` default to those of `installImageId`.

Response `200`: array of [DriverMatchDto](../reference/models.md#drivermatchdto), boot critical packages first. The rules are in [driver groups](driver-groups.md#evaluation).

`400` when `architecture` is missing or unknown, or `installImageId` does not exist.

## POST /api/client/logs

Sends a log entry. **Anonymous**.

Request body: [ClientLogRequest](../reference/models.md#clientlogrequest)

```json
{
  "level": "Info",
  "message": "Applying image: 45%",
  "macAddress": "00155D010203",
  "computerName": "LAB-PC-014",
  "phase": "ApplyImage",
  "imageName": "Windows 11 Pro"
}
```

`level` must be `Errors`, `Warnings` or `Info`, and `message` is required (`400` otherwise). Messages longer than 4000 characters are truncated.

Response `200`:

```json
{ "stored": true }
```

`stored` is false when client logging is disabled or the entry is more detailed than `clientLoggingLevel`.

## POST /api/client/multicast/{imageId}/join

Joins the multicast transmission of an image. **User**.

Request body: [MulticastJoinRequest](../reference/models.md#multicastjoinrequest)

```json
{ "macAddress": "00155D010203", "computerName": "LAB-PC-014" }
```

A client that is already connected with the same MAC is not added twice. Joining may start the transmission (AutoCast, or ScheduledCast reaching its threshold).

Response `200`: [MulticastDto](../reference/models.md#multicastdto). Find your `clientId` in `clients` by MAC address. `404` when the image has no transmission; `409` when it is stopped.

## POST /api/client/multicast/{id}/clients/{clientId}

Reports progress, or leaves the transmission. **User**. No body.

| Parameter | In | Description |
|---|---|---|
| `id` | path | Transmission id |
| `clientId` | path | Client id from the join response |
| `percent` | query, int | Progress 0 to 100 (clamped) |
| `leave` | query, bool | True to leave the transmission |

Response `204`. `404` when the client is not in that transmission.
