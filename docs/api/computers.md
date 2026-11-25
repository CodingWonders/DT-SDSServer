# Computers

`/api/computers`: known computers (WDS prestaged devices). A known computer is identified by its SMBIOS GUID and/or MAC address and gets the `Known` decision when it registers. Source: `src/Sds.Server/Controllers/ComputerControllers.cs`, `src/Sds.Core/Computers/ComputerService.cs`.

When Active Directory integration is enabled (`Sds:ActiveDirectory:Enabled`), every create, update and delete is mirrored to a computer object, and a device found only in AD is imported when it registers. See [Configuration](../reference/configuration.md#active-directory).

## GET /api/computers

Lists computers by name. **Reader**.

| Parameter | In | Description |
|---|---|---|
| `search` | query | Optional. Substring of the name, or hex digits contained in the MAC address (separators are ignored) |

Response `200`: array of [ComputerDto](../reference/models.md#computerdto).

## GET /api/computers/{id}

Returns one computer. **Reader**. Response `200`: [ComputerDto](../reference/models.md#computerdto). `404`.

## POST /api/computers

Prestages a computer. **Admin**.

Request body: [ComputerRequest](../reference/models.md#computerrequest)

```json
{
  "name": "LAB-PC-014",
  "macAddress": "00:15:5D:01:02:03",
  "smbiosGuid": "4C4C4544-0042-3510-8051-B4C04F4E4C31",
  "architecture": "amd64",
  "joinDomain": true,
  "domain": "contoso.com",
  "organizationalUnit": "OU=Lab,DC=contoso,DC=com",
  "user": "CONTOSO\\labtech",
  "joinRights": "JoinOnly",
  "imageGroup": "Windows 11",
  "installImageId": 12
}
```

Response `201` with `Location` and the [ComputerDto](../reference/models.md#computerdto) (`origin: "Prestaged"`).

Errors:

| Status | When |
|---|---|
| 400 | Invalid name (1 to 15 letters, digits or hyphens, not only digits), malformed MAC or GUID, neither MAC nor GUID given, unknown architecture, invalid `clientUnattend` XML, unknown `imageGroup` or `installImageId` |
| 409 | Another computer already has this name, MAC or GUID |

An AD write failure does not fail the request: the computer is saved with `directorySync: "Failed"` and `directoryError`. Retry with [sync](#post-apicomputersidsync).

## PUT /api/computers/{id}

Replaces every property of a computer. **Admin**. Send the full object: absent fields are cleared or reset to their default.

Request body: [ComputerRequest](../reference/models.md#computerrequest). Response `200`: [ComputerDto](../reference/models.md#computerdto). Same errors as create, plus `404`.

With AD integration, renaming a computer deletes the old AD object and creates a new one (AD objects are not renamed, as in WDS).

## DELETE /api/computers/{id}

Deletes a computer; it is no longer a known client. **Admin**.

| Parameter | In | Description |
|---|---|---|
| `deleteFromDirectory` | query, bool | Also delete the AD computer object (only with AD integration). A failure is logged, not returned. |

Response `204`. `404`.

## POST /api/computers/{id}/sync

Writes the computer to Active Directory again, for example after a failed sync. **Admin**. No body.

Response `200`: [ComputerDto](../reference/models.md#computerdto) with the new `directorySync` state. `404`.

## GET /api/computers/{id}/unattend

Returns the device specific WDS client unattend. **Reader**.

Response `200` `application/xml`. `404` (no body) when the computer has none, or does not exist (`404` with Problem Details).

To set it, include `clientUnattend` in [PUT /api/computers/{id}](#put-apicomputersid).
