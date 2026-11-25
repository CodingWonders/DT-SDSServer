# Driver groups

`/api/driver-groups`: driver groups and their filters, as in WDS. Source: `src/Sds.Server/Controllers/DriverControllers.cs`, `src/Sds.Core/Drivers/DriverGroupService.cs`.

A driver group is a set of packages plus filters that decide which clients get them. Groups are addressed by name.

## Evaluation

[`POST /api/client/drivers/query`](client.md#post-apiclientdriversquery) evaluates the groups for a client:

1. Every **enabled** group whose filters are **all** satisfied applies. A group without filters always applies.
2. From an applicable group, only **enabled** packages of the client's **architecture** are considered.
3. With applicability `Matching`, a package is kept only if it declares one of the client's hardware or compatible ids. With `All`, every package is kept.
4. A package in several applicable groups is returned once, with every group name.
5. Boot critical packages come first, then packages by display name.

A filter compares one client value with the filter's `values`:

| Filter `type` | Client value | Comparison |
|---|---|---|
| `Manufacturer`, `Model`, `ChassisType`, `BiosVendor`, `BiosVersion` | From the query | Case insensitive equality |
| `OsVersion` | Query `osVersion`, else the version of `installImageId` | Dotted prefix: `10.0` matches `10.0.26100.1742` |
| `OsEdition` | Query `osEdition`, else the image's edition id | Case insensitive equality |
| `OsLanguage` | Query `osLanguage`, else the image's default language | Case insensitive equality |
| `InstallImage` | Query `installImageId` | Values may be image ids or image names |

With policy `Include` the client value must equal one of the values; with `Exclude` it must equal none. A missing client value fails an `Include` filter and passes an `Exclude` filter.

## GET /api/driver-groups

Lists driver groups. **Reader**. Response `200`: array of [DriverGroupDto](../reference/models.md#drivergroupdto).

## GET /api/driver-groups/{name}

Returns one group. **Reader**. Response `200`: [DriverGroupDto](../reference/models.md#drivergroupdto). `404`.

## POST /api/driver-groups

Creates a group. **Admin**.

Request body: [DriverGroupRequest](../reference/models.md#drivergrouprequest)

```json
{
  "name": "Dell Latitude 7440",
  "description": "Network and storage for the 7440",
  "enabled": true,
  "applicability": "Matching",
  "filters": [
    { "type": "Manufacturer", "policy": "Include", "values": ["Dell Inc."] },
    { "type": "Model", "policy": "Include", "values": ["Latitude 7440"] },
    { "type": "OsVersion", "policy": "Include", "values": ["10.0.22631", "10.0.26100"] }
  ]
}
```

Response `201` with `Location` and the [DriverGroupDto](../reference/models.md#drivergroupdto).

Errors: `400` when the name is empty or longer than 128 characters, or a filter has no values. `409` when the name is taken.

## PUT /api/driver-groups/{name}

Replaces a group: name, description, enabled state, applicability and the whole filter list. **Admin**. Packages are not changed.

Request body: [DriverGroupRequest](../reference/models.md#drivergrouprequest). Response `200`: [DriverGroupDto](../reference/models.md#drivergroupdto). `404`, `400`, `409` (new name taken).

## DELETE /api/driver-groups/{name}

Deletes a group. The packages stay in the store. **Admin**. Response `204`. `404`.

## GET /api/driver-groups/{name}/packages

Lists the packages of a group. **Reader**. Response `200`: array of [DriverPackageDto](../reference/models.md#driverpackagedto). `404`.

## POST /api/driver-groups/{name}/packages

Adds packages to a group. Packages already in the group are ignored. **Admin**.

Request body: [DriverGroupMembershipRequest](../reference/models.md#drivergroupmembershiprequest)

```json
{ "packageIds": ["3f2504e0-4f89-11d3-9a0c-0305e82c3301"] }
```

Response `200`: the updated [DriverGroupDto](../reference/models.md#drivergroupdto). `404` (group), `400` (unknown package id).

## POST /api/driver-groups/{name}/packages/remove

Removes packages from a group. The packages stay in the store. **Admin**.

Request body: [DriverGroupMembershipRequest](../reference/models.md#drivergroupmembershiprequest). Response `200`: the updated [DriverGroupDto](../reference/models.md#drivergroupdto).
