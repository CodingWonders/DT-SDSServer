# Image groups

`/api/image-groups`: install image groups, the `IMAGES\<Group>` folders. Source: `src/Sds.Server/Controllers/ImageControllers.cs`.

Every image of a group shares the group's single instance store, `Res.RWM`. Each image is a small metadata only WIM next to it (`install.wim`, `install-(2).wim`...). The format is the one WDS uses, so the files are interchangeable with WDS.

Groups are addressed by name. Encode the name in the URL (`Windows%2011`).

## GET /api/image-groups

Lists every group. **Reader**. Response `200`: array of [ImageGroupDto](../reference/models.md#imagegroupdto).

## GET /api/image-groups/{name}

Returns one group. **Reader**. Response `200`: [ImageGroupDto](../reference/models.md#imagegroupdto). `404` when it does not exist.

## POST /api/image-groups

Creates a group and its folder. **Admin**.

Request body: [CreateImageGroupRequest](../reference/models.md#createimagegrouprequest)

```json
{ "name": "Windows 11", "description": "Desktop images" }
```

Response `201` with `Location: /api/image-groups/{name}` and the [ImageGroupDto](../reference/models.md#imagegroupdto).

Errors: `400` when the name is empty, contains characters not allowed in a file name, or is a reserved device name. `409` when the group exists.

## PATCH /api/image-groups/{name}

Renames a group or changes its description. **Admin**. Fields that are absent or `null` are left unchanged.

Request body: [UpdateImageGroupRequest](../reference/models.md#updateimagegrouprequest)

```json
{ "name": "Windows 11 24H2" }
```

A rename moves the folder on disk. UNC paths of the group's images change accordingly. Only changing the case of the name is supported too.

Response `200`: the updated [ImageGroupDto](../reference/models.md#imagegroupdto). `404`, `400` (invalid name), `409` (name taken).

## DELETE /api/image-groups/{name}

Deletes a group, its folder and everything in it. **Admin**.

| Parameter | In | Description |
|---|---|---|
| `force` | query, bool | Required when the group still contains images. Deletes the images too. |

Response `204`. `404` when the group does not exist. `409` when it contains images and `force` is not `true`.

## GET /api/image-groups/{name}/acl

Returns the NTFS permissions of the group folder. **Reader**. Response `200`: [AclDto](../reference/models.md#acldto).

Images inherit the folder permissions unless their own ACL breaks inheritance, so this is the group level user filter: it decides which users see the group's images in `GET /api/client/images`.

## PUT /api/image-groups/{name}/acl

Replaces the NTFS permissions of the group folder. **Admin**.

Request body: [AclDto](../reference/models.md#acldto). Inherited entries in the body are ignored; only explicit entries are written.

```json
{
  "inheritFromParent": false,
  "entries": [
    { "identity": "BUILTIN\\Administrators", "access": "FullControl", "allow": true },
    { "identity": "CONTOSO\\Deployment Techs", "access": "Read", "allow": true }
  ]
}
```

Response `200`: the resulting [AclDto](../reference/models.md#acldto).

## POST /api/image-groups/{name}/compact

Rebuilds `Res.RWM` so it only holds resources still used by the group's images. Deleting an image does not shrink `Res.RWM`; compaction reclaims that space. **Admin**.

No body. Response `202` with a [Job](../reference/models.md#job) of type `CompactGroup`. Result: [CompactResult](../reference/models.md#compactresult).

Other jobs on the same group wait while compaction runs.

## GET /api/image-groups/{name}/images

Lists the images of the group, sorted by priority then name. **Reader**. Response `200`: array of [ImageDto](../reference/models.md#imagedto). `404` when the group does not exist.

## POST /api/image-groups/{name}/images

Adds install images from a WIM or ESD to the group (WDS "Add Install Image"). Each selected index is split: its resources go into `Res.RWM` and its metadata into a new metadata WIM. **Admin**.

| Parameter | In | Description |
|---|---|---|
| `uploadId` | query | Optional. Use a file sent to [`POST /api/uploads`](uploads-and-jobs.md#post-apiuploads) as the source; `sourcePath` is then ignored and the upload is deleted when the job ends. |

Request body: [AddImageRequest](../reference/models.md#addimagerequest)

```json
{
  "sourcePath": "\\\\fileserver\\media\\Win11_24H2\\sources\\install.wim",
  "indexes": [6],
  "fileName": "install",
  "name": "Windows 11 Pro",
  "description": "Windows 11 Pro 24H2",
  "priority": 100,
  "enabled": true,
  "unattendXml": null
}
```

Rules:

- `indexes` null or empty adds every image of the source.
- The metadata WIM is named after `fileName` (or the source file name). On collision WDS style suffixes are added: `install.wim`, `install-(2).wim`, `install-(3).wim`...
- `name` and `description` can only be set when adding exactly one image. Otherwise names come from the WIM.
- `priority` defaults to `500000`. Lower values come first.
- `unattendXml`, when given, becomes the image unattend of every added image.

Validation happens before the job is queued: `404` when the group does not exist, `400` when `sourcePath` is missing or the file does not exist, an index is not in the source, or `name`/`description` are set for several images.

Response `202` with a [Job](../reference/models.md#job) of type `AddImage`. Result: array of [ImageDto](../reference/models.md#imagedto).

Use [`POST /api/images/inspect`](images.md#post-apiimagesinspect) first to see which indexes the source contains.
