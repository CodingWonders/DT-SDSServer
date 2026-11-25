# Server settings

The run time policy, read with `GET /api/server/settings` and replaced with `PUT /api/server/settings`. It covers the parts of the WDS server property sheet that apply to SDS. It is stored in the database; infrastructure settings live in `appsettings.json` instead (see [Configuration](configuration.md)).

Source: `src/Sds.Core/Settings/ServerSettings.cs`, validation in `src/Sds.Core/Settings/SettingsService.cs`.

`PUT` replaces the whole object: fields you leave out are reset to the default below.

## Client response (WDS "PXE Response")

| Field | Type | Default | Description |
|---|---|---|---|
| `responsePolicy` | [ResponsePolicy](enums.md#responsepolicy) | `KnownOnly` | Which clients get an answer |
| `requireApprovalForUnknown` | bool | true | With `All`, unknown clients wait for administrator approval |
| `pendingPollIntervalSeconds` | int | 10 | Seconds between two polls of a pending client. 1 to 3600. |
| `pendingMaxRetries` | int | 2160 | Polls before a pending request expires (10 s × 2160 = 6 hours). At least 1. |
| `pendingMessage` | string | `This computer is waiting for administrator approval.` | Shown to pending clients |
| `pendingRecordRetentionDays` | int | 30 | Approved and rejected records are deleted after this many days |

## Naming and Active Directory (WDS "AD DS")

| Field | Type | Default | Description |
|---|---|---|---|
| `namingPolicy` | string | `%61Username%#` | See [Naming policy](naming-policy.md). Validated on save. |
| `accountLocation` | [AccountLocation](enums.md#accountlocation) | `ServerDomain` | Where new computer accounts go |
| `domain` | string? | | Default domain DNS name for new computers |
| `organizationalUnit` | string? | | Required when `accountLocation` is `Specific` |
| `joinDomain` | bool | true | Join new computers to the domain |
| `defaultJoinRights` | [JoinRights](enums.md#joinrights) | `JoinOnly` | Join rights given on approval |
| `defaultJoinUser` | string? | | User given join rights on approval |
| `defaultBootProgram` | string? | | Returned to known clients that have no boot program (informational for SharpPXE) |

## Client logging

| Field | Type | Default | Description |
|---|---|---|---|
| `clientLoggingEnabled` | bool | true | |
| `clientLoggingLevel` | [ClientLogLevel](enums.md#clientloglevel) | `Info` | More detailed entries are dropped |
| `clientLogRetentionDays` | int | 30 | |

## Multicast

| Field | Type | Default | Description |
|---|---|---|---|
| `multicastAddressStart` | string | `239.0.0.1` | First IPv4 address of the range |
| `multicastAddressEnd` | string | `239.0.0.254` | Last address |
| `multicastPortStart` | int | 64001 | First UDP port, at least 1024 |
| `multicastPortEnd` | int | 65000 | Last UDP port, at most 65535 |
| `multicastTransferMode` | [MulticastTransferMode](enums.md#multicasttransfermode) | `SingleSession` | |
| `multicastSlowClientThresholdKbps` | int | 0 | Disconnect clients slower than this; 0 disables |
| `multicastFallbackToUnicast` | bool | true | Disconnected clients continue over unicast |

These values are stored for the multicast sender; the default sender does not transmit (see [Multicast](../api/multicast.md)).

## Example

```json
{
  "responsePolicy": "All",
  "requireApprovalForUnknown": true,
  "pendingPollIntervalSeconds": 10,
  "pendingMaxRetries": 2160,
  "pendingMessage": "This computer is waiting for administrator approval.",
  "pendingRecordRetentionDays": 30,
  "namingPolicy": "LAB-%03#",
  "accountLocation": "Specific",
  "domain": "contoso.com",
  "organizationalUnit": "OU=Lab,DC=contoso,DC=com",
  "joinDomain": true,
  "defaultJoinRights": "JoinOnly",
  "defaultJoinUser": "CONTOSO\\Deployment Techs",
  "clientLoggingEnabled": true,
  "clientLoggingLevel": "Info",
  "clientLogRetentionDays": 30,
  "multicastAddressStart": "239.0.0.1",
  "multicastAddressEnd": "239.0.0.254",
  "multicastPortStart": 64001,
  "multicastPortEnd": 65000,
  "multicastTransferMode": "SingleSession",
  "multicastSlowClientThresholdKbps": 0,
  "multicastFallbackToUnicast": true
}
```
