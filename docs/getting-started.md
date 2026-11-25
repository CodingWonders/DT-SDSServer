# Getting started

## Base URL

The server listens on `http://*:8350` by default (`Urls` in `src/Sds.Server/appsettings.json`). Every endpoint lives under `/api`:

```
http://<server>:8350/api/...
```

There is no versioning prefix. The machine readable description of the API is the OpenAPI document, which is served anonymously:

```
GET http://<server>:8350/openapi/v1.json
```

The project builds with `GenerateDocumentationFile`, so the OpenAPI document carries the XML comments of the controllers and models.

## Run the server

```powershell
dotnet run --project src/Sds.Server
```

On start the server creates `<Volume>\SDSREMINST`, the `images` and `drivers` shares, the `SDS Administrators` and `SDS Operators` groups, migrates the database in `%ProgramData%\SDS\sds.db`, fails jobs that were interrupted by the last stop and runs a [reconcile](api/server.md#post-apiserverreconcile) pass. All of this happens before the API accepts requests. See [Configuration](reference/configuration.md).

## First request

Management endpoints use Windows authentication (Negotiate). Run the client as a member of `BUILTIN\Administrators` or `SDS Administrators` (or `SDS Operators` for read only calls).

PowerShell:

```powershell
Invoke-RestMethod -Uri http://localhost:8350/api/server -UseDefaultCredentials
```

curl (Windows build, which supports SSPI):

```powershell
curl.exe --negotiate -u : http://localhost:8350/api/server
```

Response:

```json
{
  "server": "SDS01.contoso.com",
  "version": "1.0.0.0",
  "root": "C:\\SDSREMINST",
  "shares": { "images": "\\\\SDS01.contoso.com\\images", "drivers": "\\\\SDS01.contoso.com\\drivers" },
  "folders": { "pxeCore": "C:\\SDSREMINST\\PXECORE", "images": "C:\\SDSREMINST\\IMAGES", "drivers": "C:\\SDSREMINST\\DRIVERS" },
  "dataDirectory": "C:\\ProgramData\\SDS",
  "activeDirectory": false,
  "counts": { "imageGroups": 1, "images": 3, "driverPackages": 0, "driverGroups": 0, "computers": 0, "pendingDevices": 0, "multicastTransmissions": 0 }
}
```

## A complete example: add an install image

1. Create a group.

   ```powershell
   $api = 'http://localhost:8350/api'
   Invoke-RestMethod "$api/image-groups" -Method Post -UseDefaultCredentials `
     -ContentType 'application/json' -Body '{"name":"Windows 11","description":"Desktop images"}'
   ```

2. See what the source WIM contains. The path is read by the server, so it must be a path the server can reach (local or UNC).

   ```powershell
   Invoke-RestMethod "$api/images/inspect" -Method Post -UseDefaultCredentials `
     -ContentType 'application/json' -Body '{"sourcePath":"D:\\sources\\install.wim"}'
   ```

3. Add index 6. The answer is `202 Accepted` with a job.

   ```powershell
   $job = Invoke-RestMethod "$api/image-groups/Windows%2011/images" -Method Post -UseDefaultCredentials `
     -ContentType 'application/json' -Body '{"sourcePath":"D:\\sources\\install.wim","indexes":[6]}'
   ```

4. Poll the job until it is `Succeeded` or `Failed`.

   ```powershell
   do { Start-Sleep 2; $job = Invoke-RestMethod "$api/jobs/$($job.id)" -UseDefaultCredentials; $job.progress } `
   while ($job.status -in 'Queued','Running')
   $job.resultJson | ConvertFrom-Json
   ```

When the WIM is on the caller's machine instead of the server, [upload it](jobs-and-uploads.md#uploads) first and pass `?uploadId=` in step 3.

## Next steps

- [Authentication and authorization](authentication.md)
- [Conventions](conventions.md) (JSON, enums, errors)
- [Long running jobs and uploads](jobs-and-uploads.md)
- [Deployment client workflow](client-workflow.md)
