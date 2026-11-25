# Drivers

`/api/drivers`: driver packages, stored in `DRIVERS\<drvname>_<arch>_{GUID}`. Source: `src/Sds.Server/Controllers/DriverControllers.cs`, `src/Sds.Core/Drivers/DriverService.cs`.

A package is one INF for one architecture. An INF that supports several architectures is imported as one package per architecture. Packages are given to clients through [driver groups](driver-groups.md).

## GET /api/drivers

Lists packages, sorted by display name then architecture. **Reader**.

| Parameter | In | Description |
|---|---|---|
| `architecture` | query | Only this architecture |
| `class` | query | Only this setup class name (for example `Net`, `SCSIAdapter`) |
| `group` | query | Only packages in this driver group |
| `search` | query | Substring of the display name, provider or a hardware id |
| `enabled` | query, bool | Only enabled or only disabled packages |

Response `200`: array of [DriverPackageDto](../reference/models.md#driverpackagedto) without `hardwareIds`.

## GET /api/drivers/{id}

Returns one package with its hardware ids. **Reader**. `id` is the package GUID.

Response `200`: [DriverPackageDto](../reference/models.md#driverpackagedto) with `hardwareIds`. `404`.

## POST /api/drivers/import

Imports driver packages. **Admin**.

| Parameter | In | Description |
|---|---|---|
| `uploadId` | query | Optional. Import an uploaded ZIP; `sourcePath` is then ignored and the upload is deleted when the job ends. |

Request body: [DriverImportRequest](../reference/models.md#driverimportrequest)

```json
{
  "sourcePath": "\\\\fileserver\\drivers\\Dell\\Latitude 7440",
  "architectures": ["amd64"],
  "driverGroups": ["Dell Latitude 7440"],
  "enabled": true
}
```

The source can be a folder (searched recursively for `*.inf`), a single INF, or a ZIP (extracted to staging first).

For each INF and architecture:

- If the same INF content (SHA-256) is already in the store for that architecture, the existing package is reported as a duplicate and added to `driverGroups`.
- If the INF declares no device models for the requested architectures, it is skipped.
- Otherwise the INF, its catalog and the files listed in its `SourceDisksFiles` are copied to a new package folder (the whole INF folder when it has no `SourceDisksFiles`). Packages of the storage controller, hard disk controller and network setup classes are flagged `bootCritical`.

Validation before the job: `400` when `sourcePath` is missing or does not exist, an architecture is unknown, or a driver group does not exist.

Response `202` with a [Job](../reference/models.md#job) of type `ImportDrivers`. Result: [DriverImportResult](../reference/models.md#driverimportresult).

## PATCH /api/drivers/{id}

Changes the display name or enables or disables a package. **Admin**. Absent or `null` fields are left unchanged.

Request body: [UpdateDriverRequest](../reference/models.md#updatedriverrequest)

```json
{ "displayName": "Intel Ethernet I219-LM", "enabled": false }
```

Disabled packages are never given to clients. Response `200`: [DriverPackageDto](../reference/models.md#driverpackagedto). `400` when `displayName` is empty. `404`.

## DELETE /api/drivers/{id}

Deletes a package: its folder, its record and its group memberships. **Admin**. Response `204`. `404`.
