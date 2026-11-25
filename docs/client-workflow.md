# Deployment client workflow

This page describes how a deployment client (the program running in the SDS boot image on the machine being deployed) talks to the server. It is the SDS counterpart of the WDS client protocol. The endpoints are documented in [client.md](api/client.md).

PXE boot itself is not part of the API and is handled separately.

## Sequence

```
Client                                          SDS server
  |  POST /api/client/register  (anonymous)          |
  |------------------------------------------------->|  decision: Known / Allowed / Pending / Rejected / Ignored
  |                                                  |
  |  [Pending] GET /api/client/requests/{id}         |
  |  every pollIntervalSeconds                       |  administrator approves or rejects
  |------------------------------------------------->|  (/api/pending-devices)
  |                                                  |
  |  GET /api/client/unattend/{arch}?mac=&guid=      |
  |------------------------------------------------->|  WDS client unattend XML
  |                                                  |
  |  -- user signs in (Windows credentials) --       |
  |  GET  /api/client/images?architecture=           |  filtered by image file ACL
  |  POST /api/client/name        (if no name)       |  naming policy
  |  POST /api/client/drivers/query                  |  driver group evaluation
  |------------------------------------------------->|
  |                                                  |
  |  apply metadataUnc with resourceUnc (Res.RWM)    |
  |  from \\server\images; inject drivers from       |
  |  \\server\drivers                                |
  |                                                  |
  |  POST /api/client/logs  (anonymous, any time)    |
  |------------------------------------------------->|
```

## 1. Register

The client sends what it knows about itself. A MAC address or an SMBIOS GUID is required.

```json
POST /api/client/register
{ "macAddress": "00-15-5D-01-02-03", "smbiosGuid": "4C4C4544-0042-3510-8051-B4C04F4E4C31", "architecture": "x64" }
```

The server decides with this table, in order (the WDS "PXE Response" policy):

| # | Condition | `status` |
|---|---|---|
| 1 | `responsePolicy` is `RespondToNone` | `Ignored` |
| 2 | A known computer matches (SMBIOS GUID first, then MAC) | `Known`, with `configuration` |
| 3 | Unknown and `responsePolicy` is `KnownOnly` | `Ignored` |
| 4 | Unknown, `All`, `requireApprovalForUnknown` false | `Allowed`, with default `configuration` |
| 5 | Unknown, `All`, `requireApprovalForUnknown` true | `Pending` with `requestId` (or `Rejected`/`Known` if an administrator already decided) |

When Active Directory integration is on and a device is not in the SDS database but has a computer object in AD (matched by MAC or SMBIOS GUID through `netbootGUID`), it is imported as a known computer at step 2.

A known client's last contact time (`lastSeenUtc`) is updated on every registration.

### Decisions

- **Known**: `configuration` holds the computer's name, domain join settings, referral server, boot program, client unattend, restricted image group and preselected image. Server defaults fill the gaps (`domain`, OU when `accountLocation` is `Specific`, `defaultBootProgram`).
- **Allowed**: `configuration.name` is absent. Generate one with `POST /api/client/name` once a user has signed in.
- **Pending**: show `message`, wait `pollIntervalSeconds`, then poll.
- **Rejected**: show `message` and stop.
- **Ignored**: the server does not serve this client. Stop (or try another server).

## 2. Wait for approval

```
GET /api/client/requests/{requestId}
```

returns the same [ClientDecision](reference/models.md#clientdecision). Keep polling while it is `Pending`.

- A reboot that registers again reuses the same request, so the administrator does not see duplicates.
- A request expires after `pendingPollIntervalSeconds × pendingMaxRetries` (WDS default 10 s × 2160 = 6 hours). After that the poll answers `Ignored` with `"The approval request timed out."`; the next registration starts a new request.
- If the computer created by an approval is deleted later, the device is treated as pending again.
- `404` means the request was deleted by an administrator or by cleanup; register again.

## 3. Get the client unattend

```
GET /api/client/unattend/amd64?mac=00155D010203&guid=4C4C4544-0042-3510-8051-B4C04F4E4C31
```

Returns the device specific client unattend of the known computer when the MAC or GUID matches one that has it, otherwise the server wide client unattend of the architecture (`/api/server/client-unattend/{arch}`). `404` when neither exists. The same XML is already in `configuration.clientUnattend` of Known and Allowed decisions.

## 4. Sign in, list images, name, drivers

These calls need the deploying user's Windows credentials.

- `GET /api/client/images?architecture=amd64` returns the enabled, ready images of that architecture that the user can read (image file ACL), sorted by priority then name. Add `&group=` to honour `configuration.imageGroup`.
- `POST /api/client/name` generates a name with the [naming policy](reference/naming-policy.md) when the decision had none. The name is checked against SDS (and AD when enabled) for uniqueness.
- `POST /api/client/drivers/query` with the hardware ids of every device, the SMBIOS values and the chosen `installImageId` returns the driver packages to inject, boot critical packages first. See [driver groups](api/driver-groups.md#evaluation).

## 5. Apply the image

Each [ClientImageDto](reference/models.md#clientimagedto) carries UNC paths on the `images` share:

- `metadataUnc`: the metadata only WIM (`\\server\images\<Group>\install.wim`)
- `resourceUnc`: the group's single instance store (`\\server\images\<Group>\Res.RWM`)
- `unattendUnc`: the image unattend, if any

Apply `metadataUnc` with `resourceUnc` registered as a reference file (with `wimgapi.dll`: `WIMCreateFile` on the metadata WIM, `WIMSetReferenceFile` on `Res.RWM`, then `WIMLoadImage` index 1 and `WIMApplyImage`), the same way WDS clients do. The metadata WIM holds a single image at index 1. Driver packages are at `uncPath` / `infUnc` on the `drivers` share.

For multicast, see [client multicast](api/client.md#post-apiclientmulticastimageidjoin).

## 6. Log

`POST /api/client/logs` can be called at any point, anonymously. Entries above the server's `clientLoggingLevel`, or any entry when client logging is disabled, are dropped and the call answers `{"stored": false}`.
