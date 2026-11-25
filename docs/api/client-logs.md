# Client logs

`/api/client-logs`: log entries sent by deployment clients (WDS client logging). Source: `src/Sds.Server/Controllers/ServerControllers.cs`, `src/Sds.Core/Logging/ClientLogService.cs`.

Clients send entries with [`POST /api/client/logs`](client.md#post-apiclientlogs). Whether they are kept depends on `clientLoggingEnabled` and `clientLoggingLevel` in the [server settings](../reference/server-settings.md). Entries are deleted after `clientLogRetentionDays` (default 30).

## GET /api/client-logs

Queries entries, newest first. **Reader**.

| Parameter | In | Description |
|---|---|---|
| `from` | query, date | Only entries received at or after this time (UTC) |
| `to` | query, date | Only entries received at or before this time (UTC) |
| `mac` | query | Only this MAC address (any notation) |
| `computer` | query | Only this computer name (exact match) |
| `level` | query | Maximum level: `Errors` returns errors only, `Warnings` errors and warnings, `Info` everything |
| `take` | query, int | Maximum number of entries, 1 to 5000. Default 500. |

`take` is applied before the `level` filter, so a level filtered query can return fewer than `take` entries.

Response `200`: array of [ClientLogEntry](../reference/models.md#clientlogentry).

```json
[
  {
    "id": 4211,
    "timestampUtc": "2026-11-25T10:22:41Z",
    "level": "Info",
    "macAddress": "00155D010203",
    "ipAddress": "10.0.4.27",
    "computerName": "LAB-PC-014",
    "phase": "ApplyImage",
    "imageName": "Windows 11 Pro",
    "message": "Applying image: 45%"
  }
]
```
