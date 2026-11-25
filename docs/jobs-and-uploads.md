# Long running jobs and uploads

## Jobs

Operations that copy or rewrite WIM files or driver stores run in the background:

| Operation | Endpoint | Job `type` | `resultJson` on success |
|---|---|---|---|
| Add install images | `POST /api/image-groups/{name}/images` | `AddImage` | Array of [ImageDto](reference/models.md#imagedto), one per added image |
| Export an image | `POST /api/images/{id}/export` | `ExportImage` | [ExportResult](reference/models.md#exportresult) |
| Copy an image | `POST /api/images/{id}/copy` | `CopyImage` | [ImageDto](reference/models.md#imagedto) of the copy |
| Move an image | `POST /api/images/{id}/move` | `MoveImage` | [ImageDto](reference/models.md#imagedto) of the image in its new group |
| Replace an image | `POST /api/images/{id}/replace` | `ReplaceImage` | [ImageDto](reference/models.md#imagedto) |
| Compact a group | `POST /api/image-groups/{name}/compact` | `CompactGroup` | [CompactResult](reference/models.md#compactresult) |
| Import drivers | `POST /api/drivers/import` | `ImportDrivers` | [DriverImportResult](reference/models.md#driverimportresult) |

These endpoints validate the request first (the group exists, the source file exists, the indexes are in the source...) and answer `400`, `404` or `409` right away when something is wrong. When the request is valid they answer:

```
HTTP/1.1 202 Accepted
Location: http://sds01:8350/api/jobs/5b1f0f6e-0c3a-4f55-9a7e-4d1f3c3e2b10
```

```json
{
  "id": "5b1f0f6e-0c3a-4f55-9a7e-4d1f3c3e2b10",
  "type": "AddImage",
  "description": "Add 1 image(s) from install.wim to Windows 11",
  "status": "Queued",
  "progress": 0,
  "requestedBy": "CONTOSO\\admin",
  "createdUtc": "2026-11-25T10:00:00Z"
}
```

### Polling

Poll `GET /api/jobs/{id}` until `status` is `Succeeded` or `Failed`. Every one or two seconds is enough: progress is saved at most once a second.

| `status` | Meaning |
|---|---|
| `Queued` | Waiting for a free worker. At most `MaxConcurrentJobs` (default 2) run at the same time. |
| `Running` | `progress` goes from 0 to 100 |
| `Succeeded` | `progress` is 100 and `resultJson` holds the result |
| `Failed` | `error` holds the message |

`resultJson` is a **string** that contains JSON (camelCase). Parse it a second time:

```powershell
$result = $job.resultJson | ConvertFrom-Json
```

Jobs live in memory: when the server stops, queued and running jobs are lost. On the next start they are marked `Failed` with the error `The server stopped before the job completed.` Submit them again.

Jobs cannot be cancelled through the API.

Operations on the same image group are serialised by a per group lock, so two jobs that write the same `Res.RWM` run one after the other.

See [Uploads and jobs](api/uploads-and-jobs.md) for the endpoints.

## Uploads

Endpoints that take a source file (`sourcePath`) read it **on the server**. It may be a local path or a UNC path the server account can reach. When the file is on the caller's machine, upload it first:

```
POST /api/uploads?fileName=install.wim
Content-Type: application/octet-stream

<raw file bytes>
```

```json
{ "uploadId": "0f8fad5bd9cb469fa16570867728950e", "fileName": "install.wim", "size": 4831838208 }
```

The body is the raw file, not `multipart/form-data`. There is no size limit; the file is streamed to `%ProgramData%\SDS\Staging\uploads\<uploadId>\<fileName>`.

Then pass the upload id as a query parameter. `sourcePath` in the body is ignored, but the body must still be sent (`{}` is fine when no other field is needed):

| Endpoint | Upload contains |
|---|---|
| `POST /api/images/inspect?uploadId=` | WIM or ESD |
| `POST /api/image-groups/{name}/images?uploadId=` | WIM or ESD |
| `POST /api/images/{id}/replace?uploadId=` | WIM or ESD |
| `POST /api/drivers/import?uploadId=` | ZIP (or a single INF) |

Upload lifetime:

- Add image, replace image and driver import **delete the upload** when their job ends, whether it succeeds or fails. Upload again to retry.
- `inspect` does not delete it, so you can inspect and then add with the same upload id.
- `DELETE /api/uploads/{id}` deletes an upload you no longer need.
- Anything left in staging is deleted after `StagingRetentionHours` (default 24).

PowerShell:

```powershell
$up = Invoke-RestMethod "$api/uploads?fileName=install.wim" -Method Post -UseDefaultCredentials `
  -ContentType 'application/octet-stream' -InFile .\install.wim
Invoke-RestMethod "$api/image-groups/Windows%2011/images?uploadId=$($up.uploadId)" -Method Post `
  -UseDefaultCredentials -ContentType 'application/json' -Body '{"indexes":[6]}'
```

## Downloading an export

An export without `destinationPath` is written to `%ProgramData%\SDS\Staging\exports`. When the job has succeeded, download it:

```
GET /api/jobs/{id}/file
```

- The response is `application/octet-stream` with a `Content-Disposition` file name, and supports HTTP range requests (resumable downloads).
- `409` if the job is not a finished `ExportImage` job.
- `404` if the export went to a `destinationPath` (it is not staged) or the staged file is gone.

Temporary exports are deleted after `StagingRetentionHours`, when the server stops, and again when it starts. **Download them before restarting the server.** Exports to an explicit `destinationPath` are never deleted.
