# Uploads and jobs

`/api/uploads` and `/api/jobs`. Source: `src/Sds.Server/Controllers/ImageControllers.cs`, `src/Sds.Server/Infrastructure/UploadStore.cs`, `src/Sds.Core/Jobs/JobService.cs`.

How the two fit together is explained in [Long running jobs and uploads](../jobs-and-uploads.md).

## POST /api/uploads

Uploads a file to the server's staging area. **Admin**.

| Parameter | In | Description |
|---|---|---|
| `fileName` | query, required | File name to store (a plain file name; any folder part is dropped) |

- Content type: `application/octet-stream`.
- Body: the raw file bytes. No size limit.

Response `200`:

```json
{ "uploadId": "0f8fad5bd9cb469fa16570867728950e", "fileName": "install.wim", "size": 4831838208 }
```

Pass `uploadId` as the `uploadId` query parameter of:

- [`POST /api/images/inspect`](images.md#post-apiimagesinspect)
- [`POST /api/image-groups/{name}/images`](image-groups.md#post-apiimage-groupsnameimages)
- [`POST /api/images/{id}/replace`](images.md#post-apiimagesidreplace)
- [`POST /api/drivers/import`](drivers.md#post-apidriversimport)

An unknown or malformed upload id gives `404` or `400` on those endpoints.

## DELETE /api/uploads/{id}

Deletes an upload. **Admin**. Response `204`, also when the upload no longer exists.

## GET /api/jobs

Lists recent jobs, newest first. **Reader**.

| Parameter | In | Description |
|---|---|---|
| `take` | query, int | Maximum number of jobs, 1 to 1000. Default 100. |

Response `200`: array of [Job](../reference/models.md#job).

## GET /api/jobs/{id}

Returns one job. **Reader**. `id` is a GUID.

Response `200`: [Job](../reference/models.md#job). `404`.

```json
{
  "id": "5b1f0f6e-0c3a-4f55-9a7e-4d1f3c3e2b10",
  "type": "ExportImage",
  "description": "Export Windows 11 Pro from Windows 11",
  "status": "Succeeded",
  "progress": 100,
  "resultJson": "{\"path\":\"C:\\\\ProgramData\\\\SDS\\\\Staging\\\\exports\\\\...\\\\install.wim\",\"bytes\":5011234816,\"index\":1,\"name\":\"Windows 11 Pro\",\"staged\":true}",
  "requestedBy": "CONTOSO\\admin",
  "createdUtc": "2026-11-25T10:00:00Z",
  "startedUtc": "2026-11-25T10:00:00Z",
  "completedUtc": "2026-11-25T10:04:12Z"
}
```

## GET /api/jobs/{id}/file

Downloads the WIM produced by a staged export. **Admin**.

Response `200` `application/octet-stream` with `Content-Disposition: attachment; filename=...`. Range requests are supported (`206`).

| Status | When |
|---|---|
| 409 | The job is not an `ExportImage` job with status `Succeeded` |
| 404 | The job does not exist, the export was written to a `destinationPath`, or the staged file was already deleted |

```powershell
Invoke-WebRequest "$api/jobs/$jobId/file" -UseDefaultCredentials -OutFile .\install.wim
```
