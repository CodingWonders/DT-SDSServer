# Pending devices

`/api/pending-devices`: unknown clients waiting for administrator approval (WDS "Pending Devices"). Source: `src/Sds.Server/Controllers/ComputerControllers.cs`, `src/Sds.Core/Computers/PendingDeviceService.cs`.

Pending records are created when `responsePolicy` is `All` and `requireApprovalForUnknown` is true, and an unknown client [registers](client.md#post-apiclientregister). The client then polls until an administrator approves or rejects it. See [Deployment client workflow](../client-workflow.md#2-wait-for-approval).

Record lifetime:

- A `Pending` request expires `pendingPollIntervalSeconds × pendingMaxRetries` after the device was first seen (the client then gets `Ignored`). The record is removed by the hourly maintenance task once the device has not polled for that long.
- `Approved` and `Rejected` records are deleted after `pendingRecordRetentionDays` (default 30).

## GET /api/pending-devices

Lists records. **Reader**.

| Parameter | In | Description |
|---|---|---|
| `status` | query | Optional. `Pending`, `Approved` or `Rejected` |

Response `200`: array of [PendingDeviceDto](../reference/models.md#pendingdevicedto).

## GET /api/pending-devices/{id}

Returns one record. **Reader**. Response `200`: [PendingDeviceDto](../reference/models.md#pendingdevicedto). `404`.

## POST /api/pending-devices/{id}/approve

Approves a device: creates a known computer and marks the record `Approved`. On its next poll the client gets `Known`. **Admin**.

Request body: [ApproveRequest](../reference/models.md#approverequest). Every field is optional; send `{}` to use the server defaults.

```json
{
  "name": "LAB-PC-015",
  "user": "CONTOSO\\labtech",
  "joinRights": "Full",
  "imageGroup": "Windows 11",
  "message": "Approved for the lab rollout"
}
```

Defaults for absent fields:

| Field | Default |
|---|---|
| `name` | Generated with the [naming policy](../reference/naming-policy.md) for `user` (or `defaultJoinUser`, or the approving administrator) and the device MAC |
| `user` | `defaultJoinUser` |
| `joinRights` | `defaultJoinRights` |
| `joinDomain` | `joinDomain` setting |
| `domain` | `domain` setting |
| `organizationalUnit` | The `organizationalUnit` setting when `accountLocation` is `Specific` |

The computer gets `origin: "Approved"` and the description `Approved by <user> on <date>`.

Response `200`: the updated [PendingDeviceDto](../reference/models.md#pendingdevicedto) with `computerId`.

Errors: `404`; `409` when the record is not `Pending` or the name is taken; `400` for invalid values (see [computers](computers.md#post-apicomputers)).

## POST /api/pending-devices/approve-all

Approves every `Pending` record with the same options. Names always come from the naming policy. **Admin**.

Request body: [ApproveRequest](../reference/models.md#approverequest) without `name` (`400` if `name` is set).

Response `200`: array of the approved [PendingDeviceDto](../reference/models.md#pendingdevicedto). Devices are approved one by one; if one fails, the request fails and the devices approved before it stay approved.

## POST /api/pending-devices/{id}/reject

Rejects a device. On its next poll the client gets `Rejected` with the message. **Admin**.

Request body: [RejectRequest](../reference/models.md#rejectrequest)

```json
{ "message": "Unknown hardware. Contact the service desk." }
```

Without a message the client sees `The administrator rejected this computer.`

Response `200`: the updated [PendingDeviceDto](../reference/models.md#pendingdevicedto). `404`; `409` when the record is not `Pending`.

## DELETE /api/pending-devices/{id}

Deletes one record. **Admin**. Response `204`. `404`.

A client whose record is deleted gets `404` on its next poll and registers again.

## DELETE /api/pending-devices

Deletes every record, or only those with a status. **Admin**.

| Parameter | In | Description |
|---|---|---|
| `status` | query | Optional. `Pending`, `Approved` or `Rejected` |

Response `200`:

```json
{ "deleted": 7 }
```
