# Images

`/api/images`: install images. Source: `src/Sds.Server/Controllers/ImageControllers.cs`.

Images are added through their group: [`POST /api/image-groups/{name}/images`](image-groups.md#post-apiimage-groupsnameimages).

## GET /api/images

Lists images, sorted by priority then name. **Reader**.

| Parameter | In | Description |
|---|---|---|
| `group` | query | Optional. Only images of this group. |

Response `200`: array of [ImageDto](../reference/models.md#imagedto). Not filtered by the image ACL; for the client view see [`GET /api/client/images`](client.md#get-apiclientimages).

## GET /api/images/{id}

Returns one image. **Reader**. Response `200`: [ImageDto](../reference/models.md#imagedto). `404`.

## POST /api/images/inspect

Lists the images contained in a WIM or ESD before adding them. **Admin**.

| Parameter | In | Description |
|---|---|---|
| `uploadId` | query | Optional. Inspect an uploaded file. The upload is kept. |

Request body:

```json
{ "sourcePath": "D:\\sources\\install.wim" }
```

`sourcePath` is ignored when `uploadId` is given (send `{}`).

Response `200`: array of [WimImageInfo](../reference/models.md#wimimageinfo).

```json
[
  {
    "index": 6,
    "name": "Windows 11 Pro",
    "description": "Windows 11 Pro",
    "displayName": "Windows 11 Pro",
    "flags": "Professional",
    "architecture": "amd64",
    "productName": "Microsoft® Windows® Operating System",
    "editionId": "Professional",
    "installationType": "Client",
    "productType": "WinNT",
    "languages": ["en-US"],
    "defaultLanguage": "en-US",
    "version": "10.0.26100.1742",
    "servicePackLevel": 0,
    "totalBytes": 19212310231,
    "directoryCount": 24102,
    "fileCount": 110233
  }
]
```

`400` when the file does not exist.

## PATCH /api/images/{id}

Changes image properties. **Admin**. Fields that are absent or `null` are left unchanged.

Request body: [UpdateImageRequest](../reference/models.md#updateimagerequest)

```json
{ "name": "Windows 11 Pro (corp)", "description": null, "enabled": true, "priority": 10 }
```

`name` and `description` are also written into the metadata WIM, so tools reading the WIM see the same values (as WDS does).

Response `200`: [ImageDto](../reference/models.md#imagedto). `400` when `name` is empty. `404`.

## DELETE /api/images/{id}

Deletes an image: its metadata WIM, its unattend folder and its record. **Admin**. Response `204`. `404`.

`Res.RWM` is not shrunk; run [compact](image-groups.md#post-apiimage-groupsnamecompact) to reclaim space. When the last image of a group is deleted, `Res.RWM` is deleted too.

## POST /api/images/{id}/export

Merges the metadata WIM with `Res.RWM` into a standalone WIM (WDS "Export Image"). **Admin**.

Request body: [ExportImageRequest](../reference/models.md#exportimagerequest)

```json
{ "destinationPath": "E:\\Exports\\win11pro.wim", "append": false, "name": null, "description": null }
```

- With `destinationPath`, the WIM is written there (a path the server can reach). If the file exists, set `append: true` to add the image to it; otherwise the request fails with `409`.
- Without `destinationPath` (send `{}`), the WIM is staged and downloaded through [`GET /api/jobs/{id}/file`](uploads-and-jobs.md#get-apijobsidfile). Staged exports are temporary, see [Downloading an export](../jobs-and-uploads.md#downloading-an-export).
- `name` and `description` override the exported image's name and description.

Response `202` with a [Job](../reference/models.md#job) of type `ExportImage`. Result: [ExportResult](../reference/models.md#exportresult).

## POST /api/images/{id}/copy

Copies the image to another group. **Admin**. The image is exported to staging, then added to the target group (its resources go into the target `Res.RWM`). Priority, enabled state and image unattend are copied.

Request body: [CopyImageRequest](../reference/models.md#copyimagerequest)

```json
{ "targetGroup": "Lab", "fileName": null, "name": null, "description": null }
```

`fileName`, `name` and `description` default to those of the source image. Copying within the same group is allowed and creates `-(n)` file names.

Response `202` with a [Job](../reference/models.md#job) of type `CopyImage`. Result: [ImageDto](../reference/models.md#imagedto) of the copy. `404` when the image or the target group does not exist.

## POST /api/images/{id}/move

Same as copy, then deletes the original. The moved image gets a new id. **Admin**.

Request body: [CopyImageRequest](../reference/models.md#copyimagerequest). `400` when the target is the source group.

Response `202` with a [Job](../reference/models.md#job) of type `MoveImage`. Result: [ImageDto](../reference/models.md#imagedto) in the new group.

## POST /api/images/{id}/replace

Replaces the content of the image with an image from another WIM (WDS "Replace Image"). The id, file name, priority, enabled state, unattend and permissions are kept; the name and WIM derived properties come from the new image. **Admin**.

| Parameter | In | Description |
|---|---|---|
| `uploadId` | query | Optional. Use an uploaded file; it is deleted when the job ends. |

Request body: [ReplaceImageRequest](../reference/models.md#replaceimagerequest)

```json
{ "sourcePath": "D:\\sources\\install.wim", "index": 6 }
```

`index` defaults to 1. `400` when the source or the index does not exist.

Response `202` with a [Job](../reference/models.md#job) of type `ReplaceImage`. Result: [ImageDto](../reference/models.md#imagedto).

## GET /api/images/{id}/unattend

Returns the image unattend (`IMAGES\<Group>\<image>\Unattend\ImageUnattend.xml`). **Reader**.

Response `200` `application/xml`. `404` (no body) when the image has none.

## PUT /api/images/{id}/unattend

Sets the image unattend. **Admin**.

- Content type: `application/xml`, `text/xml` or `text/plain`.
- Body: the raw XML.
- Response `204`. `400` when the XML is not well formed. `404`.

```powershell
Invoke-RestMethod "$api/images/12/unattend" -Method Put -UseDefaultCredentials `
  -ContentType 'application/xml' -InFile .\ImageUnattend.xml
```

## DELETE /api/images/{id}/unattend

Removes the image unattend. **Admin**. Response `204`.

## GET /api/images/{id}/acl

Returns the NTFS permissions of the metadata WIM (the WDS image user filter). **Reader**. Response `200`: [AclDto](../reference/models.md#acldto).

## PUT /api/images/{id}/acl

Replaces the NTFS permissions of the metadata WIM. **Admin**. Users who cannot read the file do not see the image in [`GET /api/client/images`](client.md#get-apiclientimages).

Request body and response: [AclDto](../reference/models.md#acldto). See the example in [image groups](image-groups.md#put-apiimage-groupsnameacl).
