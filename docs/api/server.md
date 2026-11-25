# Server

`/api/server`: server information, run time policy, reconciliation and the server wide client unattend. Source: `src/Sds.Server/Controllers/ServerControllers.cs`.

## GET /api/server

Server status and statistics. **Reader**.

Response `200`:

| Field | Type | Description |
|---|---|---|
| `server` | string | Host name used in UNC paths (`PublicServerName`, or the machine DNS name) |
| `version` | string | Server assembly version |
| `root` | string | `<Volume>\SDSREMINST` |
| `shares.images` | string | UNC path of the `images` share |
| `shares.drivers` | string | UNC path of the `drivers` share |
| `folders.pxeCore` | string | Local path of `PXECORE` |
| `folders.images` | string | Local path of `IMAGES` |
| `folders.drivers` | string | Local path of `DRIVERS` |
| `dataDirectory` | string | Database, logs and staging folder (default `%ProgramData%\SDS`) |
| `activeDirectory` | bool | True when Active Directory integration is enabled |
| `counts.imageGroups` | int | |
| `counts.images` | int | |
| `counts.driverPackages` | int | |
| `counts.driverGroups` | int | |
| `counts.computers` | int | |
| `counts.pendingDevices` | int | Records with status `Pending` only |
| `counts.multicastTransmissions` | int | |

See [Getting started](../getting-started.md#first-request) for an example.

## GET /api/server/settings

Returns the run time policy. **Reader**.

Response `200`: [ServerSettings](../reference/server-settings.md).

## PUT /api/server/settings

Replaces the run time policy. **Admin**. Send the whole object: missing fields take their default value.

Request body: [ServerSettings](../reference/server-settings.md). Response `200`: the saved settings.

Errors `400`:

- `pendingPollIntervalSeconds` not between 1 and 3600
- `pendingMaxRetries` less than 1
- invalid multicast port or address range
- `accountLocation` is `Specific` without `organizationalUnit`
- `namingPolicy` cannot be parsed (see [Naming policy](../reference/naming-policy.md))

```powershell
$s = Invoke-RestMethod "$api/server/settings" -UseDefaultCredentials
$s.responsePolicy = 'All'
Invoke-RestMethod "$api/server/settings" -Method Put -UseDefaultCredentials `
  -ContentType 'application/json' -Body ($s | ConvertTo-Json)
```

## POST /api/server/reconcile

Brings the database and the share back in line, for example after files were copied in or removed by hand, or after restoring a database backup. **Admin**. Runs synchronously (no job). The same pass runs at every server start.

It:

- adds image groups and metadata WIMs found in `IMAGES` but missing from the database,
- recreates the folder of groups that exist in the database but not on disk,
- marks images whose files are missing as `Missing`, and missing images that are back as `Ready`,
- registers driver folders found in `DRIVERS`, and reports packages whose folder is gone.

Response `200`: [ReconcileReport](../reference/models.md#reconcilereport).

```json
{
  "groupsImported": [],
  "groupsRecreated": [],
  "imagesImported": ["Windows 11\\install-(2).wim"],
  "imagesMissing": [],
  "imagesRestored": [],
  "driversImported": [],
  "driversMissing": [],
  "driverFoldersIgnored": [],
  "errors": []
}
```

## GET /api/server/client-unattend/{architecture}

Returns the server wide WDS client unattend for an architecture. **Reader**.

| Parameter | In | Description |
|---|---|---|
| `architecture` | path | `x86`, `amd64`, `arm64`... (aliases accepted, see [Conventions](../conventions.md#identifiers)) |

Response `200` `application/xml`: the XML. `404` (no body) when none is set.

This is the client unattend given to clients that have no device specific one.

## PUT /api/server/client-unattend/{architecture}

Sets the server wide client unattend. **Admin**.

- Content type: `application/xml`, `text/xml` or `text/plain`.
- Body: the raw XML (not JSON).
- Response `204`. `400` when the XML is not well formed or the architecture is unknown.

```powershell
Invoke-RestMethod "$api/server/client-unattend/amd64" -Method Put -UseDefaultCredentials `
  -ContentType 'application/xml' -InFile .\WDSClientUnattend.xml
```

## DELETE /api/server/client-unattend/{architecture}

Removes the server wide client unattend of an architecture. **Admin**. Response `204`, also when none was set.
