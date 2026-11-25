# The DISMTools System Deployment Services Server

This repository serves as the home for the SDS Server REST-powered components.

The DISMTools System Deployment Services are designed so migration from the Windows Deployment Services is quick and simple. SDS does not impose an additional learning curve.

## Share layout

In the System Deployment Services, the following structure is used:

```
<Volume>\SDSREMINST\                       not shared
  PXECORE\                                 not shared; SharpPXE and the single boot image (not managed by SDS)
  IMAGES\                                  shared as \\<host>\images
  IMAGES\<Group>\Res.RWM                   single instance resources of every image of the group
  IMAGES\<Group>\<image>.wim               metadata only WIM per image (install.wim, install-(2).wim...)
  IMAGES\<Group>\<image>\Unattend\ImageUnattend.xml
  DRIVERS\                                 shared as \\<host>\drivers
  DRIVERS\<drvname>_<arch>_{GUID}\         one folder per driver package
%ProgramData%\SDS\sds.db, Staging\, Logs\  database and private files, kept out of SDSREMINST
```

Images are split and merged with `wimgapi.dll`, the library WDS itself uses, so files are interchangeable with WDS.

## Source

[SharpPXE](https://github.com/CodingWonders/SharpPXE), the network boot server that goes in PXECORE, is a git submodule in `external/SharpPXE`. Clone with submodules:

```
git clone --recurse-submodules <repository URL>
```

or, in an existing clone, `git submodule update --init`. SharpPXE builds with its own settings; it is not part of `Sds.sln`.

## Run

To launch the Server component as a standard application, run:

```
dotnet run --project src/Sds.Server
```

Configuration lives in `src/Sds.Server/appsettings.json` under `Sds` (volume, `ImagesShareName` and `DriversShareName`, groups, compression, Active Directory). The default port is 8350.

To install as a Windows service:

```
dotnet publish src/Sds.Server -c Release -o "C:\Program Files\SDS"
sc.exe create SDS binPath= "C:\Program Files\SDS\SdsServer.exe" start= auto
```

## Web Console

The SDS Web Console is a sample management interface from which you can get the hang of how SDS works. Use it as a reference to build your SDS management client.

To run both the Server and the Web Console, run:

```
dotnet run --project src/Sds.Server
dotnet run --project src/Sds.Web
```

Then open http://localhost:8351. The console only serves static files; the browser calls the API directly with your Windows sign in. Its API address is `SdsApiUrl` in `src/Sds.Web/appsettings.json`, and the server must list the console origin in `Sds:CorsOrigins` (default `http://localhost:8351`). When the console runs on another machine, use that machine's address in both settings and add the server to the browser's intranet zone so it sends Windows credentials.

## Security

On start SDS creates the `images` and `drivers` shares when they are missing, with the same share permissions WDS gives REMINST (SYSTEM and Administrators full, Authenticated Users read) plus `SDS Administrators` full. The SDSREMINST root and PXECORE are never shared, and an existing share is never changed. It also adds the inbound firewall rule `SDS Server (TCP-In)` for the API port when it is missing (`Sds:CreateFirewallRule`). UNC paths returned by the API point to these shares, for example `\\<host>\images\<Group>\install.wim`. On a domain controller there is no local SAM, so the two "local" groups are created as domain local groups in `CN=Users` of the domain.

Windows Negotiate (Kerberos/NTLM). Writes need `BUILTIN\Administrators` or the local `SDS Administrators` group; reads also allow `SDS Operators`. Extra domain groups can be added in configuration. Client registration, polling, client unattend and client logs are anonymous, as in WDS. Image lists are filtered by the NTFS permissions of each image file (WDS user filter).

## API map

| Area | Endpoints |
|---|---|
| Server | `GET /api/server`, `GET/PUT /api/server/settings`, `POST /api/server/reconcile`, `GET/PUT/DELETE /api/server/client-unattend/{arch}` |
| Image groups | `GET/POST /api/image-groups`, `GET/PATCH/DELETE /api/image-groups/{name}`, `GET/PUT .../acl`, `POST .../compact`, `GET/POST .../images` |
| Images | `GET /api/images`, `POST /api/images/inspect`, `GET/PATCH/DELETE /api/images/{id}`, `POST .../export`, `.../copy`, `.../move`, `.../replace`, `GET/PUT/DELETE .../unattend`, `GET/PUT .../acl` |
| Uploads and jobs | `POST /api/uploads?fileName=`, `GET /api/jobs`, `GET /api/jobs/{id}`, `GET /api/jobs/{id}/file` |
| Drivers | `GET /api/drivers`, `POST /api/drivers/import`, `GET/PATCH/DELETE /api/drivers/{id}` |
| Driver groups | `GET/POST /api/driver-groups`, `GET/PUT/DELETE /api/driver-groups/{name}`, `GET/POST .../packages`, `POST .../packages/remove` |
| Computers | `GET/POST /api/computers`, `GET/PUT/DELETE /api/computers/{id}`, `POST .../sync`, `GET .../unattend` |
| Pending devices | `GET /api/pending-devices`, `POST /api/pending-devices/{id}/approve`, `.../reject`, `POST /api/pending-devices/approve-all`, `DELETE` |
| Multicast | `GET/POST /api/multicast`, `GET/DELETE /api/multicast/{id}`, `POST .../start`, `.../stop`, `DELETE .../clients/{clientId}` |
| Client logs | `GET /api/client-logs` |
| Client | `POST /api/client/register`, `GET /api/client/requests/{id}`, `GET /api/client/unattend/{arch}`, `GET /api/client/images`, `POST /api/client/name`, `POST /api/client/drivers/query`, `POST /api/client/logs`, `POST /api/client/multicast/{imageId}/join` |

Long operations (add, export, copy, move, replace, compact, driver import) return `202 Accepted` with a job to poll. The OpenAPI document is at `/openapi/v1.json`.

An export without a `destinationPath` is a temporary export in `Staging\exports`, downloaded through `GET /api/jobs/{id}/file`. Temporary exports are deleted when the server stops, and again when it starts, which catches anything a crash or a killed process left behind. Download them before you restart the server.

## Tests

```
dotnet test
```

Unit tests cover the INF parser, naming policy, filters and identifiers; integration tests run the real wimgapi split, export and compaction; API tests drive the whole server in memory.

## Release

`scripts/release.ps1` runs the tests, then publishes the Server and the Web Console as self-contained applications (the .NET runtime is included, so the target machine needs nothing installed) and zips each one:

```
.\scripts\release.ps1 -Version 1.2.0
```

```
artifacts\release\1.2.0\SdsServer-1.2.0-win-x64.zip
artifacts\release\1.2.0\SdsWeb-1.2.0-win-x64.zip
artifacts\release\1.2.0\SHA256SUMS.txt
```

The server package also contains SharpPXE in its `SharpPXE` folder: `SharpPXE.exe` (Native AOT), `SharpPXE.Gui.exe` (self-contained), `SharpPXE.ini` and `files\`. Copy that folder to `<Volume>\SDSREMINST\PXECORE` and set it up as its [README](https://github.com/CodingWonders/SharpPXE#setup) describes; SharpPXE creates its own firewall rule. Building it needs the Visual Studio "Desktop development with C++" workload (plus the ARM64 C++ tools for `win-arm64`). SharpPXE is 64-bit only, so `win-x86` packages leave it out. Use `-SkipSharpPXE` to build without it. The script fetches the submodule when it is missing.

Without `-Version` the latest git tag is used (`v1.2.0` gives `1.2.0`), or `1.0.0` when there is none. `-Runtime win-x64, win-arm64` builds several architectures, and `-SkipTests` skips the tests. The wimgapi tests need administrator rights, so run the script from an elevated prompt unless you skip them.

To install a package, extract it and run `SdsServer.exe` (or register it as a service, see [Run](#run)) and `SdsWeb.exe`. Edit `appsettings.json` next to each executable.
