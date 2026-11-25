# Multicast

`/api/multicast`: multicast transmission definitions (WDS "Multicast Transmissions"). Source: `src/Sds.Server/Controllers/ServerControllers.cs`, `src/Sds.Core/Multicast/MulticastService.cs`.

SDS manages transmissions, their start criteria and the clients that joined them. The data sender is pluggable (`IMulticastSender`); the default implementation only logs, so no multicast traffic is sent unless a sender is plugged in.

There is at most one transmission per image, as in WDS.

## Types and states

| Type | Starts |
|---|---|
| `AutoCast` | As soon as one client joins |
| `ScheduledCast` | When `startClientThreshold` clients are connected, or at `startTimeUtc` once at least one client is connected, whichever comes first. Or manually. |

| State | Meaning |
|---|---|
| `Waiting` | Created, start criteria not met yet |
| `Active` | Started |
| `Stopped` | Stopped by an administrator; clients can no longer join |

Start criteria of waiting transmissions are checked when a client joins and every 15 seconds.

## GET /api/multicast

Lists transmissions by name. **Reader**. Response `200`: array of [MulticastDto](../reference/models.md#multicastdto).

## GET /api/multicast/{id}

Returns one transmission with its clients. **Reader**. Response `200`: [MulticastDto](../reference/models.md#multicastdto). `404`.

## POST /api/multicast

Creates a transmission. **Admin**.

Request body: [MulticastRequest](../reference/models.md#multicastrequest)

```json
{ "name": "Win11 Lab", "installImageId": 12, "type": "ScheduledCast", "startClientThreshold": 10, "startTimeUtc": "2026-11-26T07:00:00Z" }
```

`startClientThreshold` and `startTimeUtc` are ignored for `AutoCast`.

Response `201` with `Location` and the [MulticastDto](../reference/models.md#multicastdto).

Errors: `400` when `name` is empty, a `ScheduledCast` has neither threshold nor start time, the threshold is below 1, or the image does not exist. `409` when the name is taken or the image already has a transmission.

## POST /api/multicast/{id}/start

Starts a transmission now, whatever its criteria. **Admin**. No body. Response `200`: [MulticastDto](../reference/models.md#multicastdto).

## POST /api/multicast/{id}/stop

Stops a transmission and disconnects its clients. **Admin**. No body. Response `200`: [MulticastDto](../reference/models.md#multicastdto).

## DELETE /api/multicast/{id}

Deletes a transmission (stopping it first if active). **Admin**. Response `204`. `404`.

## DELETE /api/multicast/{id}/clients/{clientId}

Disconnects one client (sets its `leftUtc`). **Admin**. Response `204`. `404`.

## Client side

Clients join with [`POST /api/client/multicast/{imageId}/join`](client.md#post-apiclientmulticastimageidjoin) and report progress with [`POST /api/client/multicast/{id}/clients/{clientId}`](client.md#post-apiclientmulticastidclientsclientid).
