# SDS Server API documentation

The DISMTools System Deployment Services (SDS) server exposes a REST API over HTTP (default port `8350`). This API is the only way to manage SDS: the Web Console (`src/Sds.Web`) is a sample client built on it.

The API has two audiences:

- **Management clients** (consoles, scripts, automation) manage images, drivers, computers and server policy. They authenticate with Windows credentials, or, for browsers, with a Windows user name and password that opens a session.
- **Deployment clients** (the program that runs in the SDS boot image on a machine being deployed) register, wait for approval, list images, ask for drivers and send logs. Several of these calls are anonymous, as in WDS.

## Guides

| Page | Contents |
|---|---|
| [Getting started](getting-started.md) | Base URL, a first request from PowerShell and curl, the OpenAPI document |
| [Authentication and authorization](authentication.md) | Negotiate, the `SdsAdmin` and `SdsReader` roles, anonymous endpoints, CORS, image user filters |
| [Conventions](conventions.md) | JSON format, enums, identifiers (MAC, GUID, architecture, names), errors |
| [Long running jobs and uploads](jobs-and-uploads.md) | `202 Accepted`, polling jobs, job results, uploading WIM and ZIP files, downloading exports |
| [Deployment client workflow](client-workflow.md) | The sequence of calls a deployment client makes, from boot to applying the image |

## Endpoint reference

| Area | Page | Base path |
|---|---|---|
| Server | [server.md](api/server.md) | `/api/server` |
| Image groups | [image-groups.md](api/image-groups.md) | `/api/image-groups` |
| Images | [images.md](api/images.md) | `/api/images` |
| Uploads and jobs | [uploads-and-jobs.md](api/uploads-and-jobs.md) | `/api/uploads`, `/api/jobs` |
| Drivers | [drivers.md](api/drivers.md) | `/api/drivers` |
| Driver groups | [driver-groups.md](api/driver-groups.md) | `/api/driver-groups` |
| Computers | [computers.md](api/computers.md) | `/api/computers` |
| Pending devices | [pending-devices.md](api/pending-devices.md) | `/api/pending-devices` |
| Multicast | [multicast.md](api/multicast.md) | `/api/multicast` |
| Client logs | [client-logs.md](api/client-logs.md) | `/api/client-logs` |
| Deployment client | [client.md](api/client.md) | `/api/client` |

## Reference

| Page | Contents |
|---|---|
| [Models](reference/models.md) | Every request and response body, field by field |
| [Enumerations](reference/enums.md) | Every enum and its values |
| [Server settings](reference/server-settings.md) | Run time policy (`/api/server/settings`) |
| [Configuration](reference/configuration.md) | `appsettings.json`, the `Sds` section |
| [Naming policy](reference/naming-policy.md) | The computer naming policy language |

## All endpoints

Role: **Admin** = `SdsAdmin` policy, **Reader** = `SdsReader` policy, **User** = any authenticated Windows user, **Anonymous** = no credentials. See [Authentication](authentication.md).

| Method | Path | Role | Summary |
|---|---|---|---|
| GET | `/api/auth/me` | User | Signed in account, its SDS role and sign-in method |
| POST | `/api/auth/login` | Anonymous | Password sign-in (session cookie) |
| GET | `/api/auth/session` | Anonymous | Current password session |
| POST | `/api/auth/logout` | Anonymous | End the password session |
| GET | `/api/server` | Reader | Server status and statistics |
| GET | `/api/server/settings` | Reader | Server policy settings |
| PUT | `/api/server/settings` | Admin | Replace server policy settings |
| POST | `/api/server/reconcile` | Admin | Database and share consistency pass |
| GET | `/api/server/client-unattend/{architecture}` | Reader | Server wide client unattend |
| PUT | `/api/server/client-unattend/{architecture}` | Admin | Set server wide client unattend |
| DELETE | `/api/server/client-unattend/{architecture}` | Admin | Remove server wide client unattend |
| GET | `/api/image-groups` | Reader | List image groups |
| POST | `/api/image-groups` | Admin | Create an image group |
| GET | `/api/image-groups/{name}` | Reader | Get an image group |
| PATCH | `/api/image-groups/{name}` | Admin | Rename or describe a group |
| DELETE | `/api/image-groups/{name}` | Admin | Delete a group (`?force=true` deletes its images) |
| GET | `/api/image-groups/{name}/acl` | Reader | Group folder permissions |
| PUT | `/api/image-groups/{name}/acl` | Admin | Replace group folder permissions |
| POST | `/api/image-groups/{name}/compact` | Admin | Compact `Res.RWM` (job) |
| GET | `/api/image-groups/{name}/images` | Reader | List the images of a group |
| POST | `/api/image-groups/{name}/images` | Admin | Add install images (job) |
| GET | `/api/images` | Reader | List images |
| POST | `/api/images/inspect` | Admin | List the images inside a source WIM |
| GET | `/api/images/{id}` | Reader | Get an image |
| PATCH | `/api/images/{id}` | Admin | Change name, description, enabled, priority |
| DELETE | `/api/images/{id}` | Admin | Delete an image |
| POST | `/api/images/{id}/export` | Admin | Export to a standalone WIM (job) |
| POST | `/api/images/{id}/copy` | Admin | Copy to another group (job) |
| POST | `/api/images/{id}/move` | Admin | Move to another group (job) |
| POST | `/api/images/{id}/replace` | Admin | Replace the image content (job) |
| GET | `/api/images/{id}/unattend` | Reader | Image unattend XML |
| PUT | `/api/images/{id}/unattend` | Admin | Set image unattend XML |
| DELETE | `/api/images/{id}/unattend` | Admin | Remove image unattend |
| GET | `/api/images/{id}/acl` | Reader | Image file permissions (user filter) |
| PUT | `/api/images/{id}/acl` | Admin | Replace image file permissions |
| POST | `/api/uploads?fileName=` | Admin | Upload a file |
| DELETE | `/api/uploads/{id}` | Admin | Delete an upload |
| GET | `/api/jobs` | Reader | List recent jobs |
| GET | `/api/jobs/{id}` | Reader | Get a job |
| GET | `/api/jobs/{id}/file` | Admin | Download a staged export |
| GET | `/api/drivers` | Reader | List driver packages |
| POST | `/api/drivers/import` | Admin | Import driver packages (job) |
| GET | `/api/drivers/{id}` | Reader | Get a package with its hardware ids |
| PATCH | `/api/drivers/{id}` | Admin | Rename, enable or disable a package |
| DELETE | `/api/drivers/{id}` | Admin | Delete a package |
| GET | `/api/driver-groups` | Reader | List driver groups |
| POST | `/api/driver-groups` | Admin | Create a driver group |
| GET | `/api/driver-groups/{name}` | Reader | Get a driver group |
| PUT | `/api/driver-groups/{name}` | Admin | Replace a driver group |
| DELETE | `/api/driver-groups/{name}` | Admin | Delete a driver group |
| GET | `/api/driver-groups/{name}/packages` | Reader | List the packages of a group |
| POST | `/api/driver-groups/{name}/packages` | Admin | Add packages to a group |
| POST | `/api/driver-groups/{name}/packages/remove` | Admin | Remove packages from a group |
| GET | `/api/computers` | Reader | List known computers |
| POST | `/api/computers` | Admin | Prestage a computer |
| GET | `/api/computers/{id}` | Reader | Get a computer |
| PUT | `/api/computers/{id}` | Admin | Replace a computer |
| DELETE | `/api/computers/{id}` | Admin | Delete a computer |
| POST | `/api/computers/{id}/sync` | Admin | Push the computer to Active Directory |
| GET | `/api/computers/{id}/unattend` | Reader | Device specific client unattend |
| GET | `/api/pending-devices` | Reader | List pending device records |
| GET | `/api/pending-devices/{id}` | Reader | Get a record |
| POST | `/api/pending-devices/{id}/approve` | Admin | Approve a device |
| POST | `/api/pending-devices/{id}/reject` | Admin | Reject a device |
| POST | `/api/pending-devices/approve-all` | Admin | Approve every pending device |
| DELETE | `/api/pending-devices/{id}` | Admin | Delete a record |
| DELETE | `/api/pending-devices` | Admin | Delete all records (or by status) |
| GET | `/api/multicast` | Reader | List transmissions |
| POST | `/api/multicast` | Admin | Create a transmission |
| GET | `/api/multicast/{id}` | Reader | Get a transmission |
| DELETE | `/api/multicast/{id}` | Admin | Delete a transmission |
| POST | `/api/multicast/{id}/start` | Admin | Start a transmission |
| POST | `/api/multicast/{id}/stop` | Admin | Stop a transmission |
| DELETE | `/api/multicast/{id}/clients/{clientId}` | Admin | Disconnect a client |
| GET | `/api/client-logs` | Reader | Query client log entries |
| POST | `/api/client/register` | Anonymous | Register a booting client |
| GET | `/api/client/requests/{id}` | Anonymous | Poll a pending approval |
| GET | `/api/client/unattend/{architecture}` | Anonymous | Client unattend for this device |
| POST | `/api/client/logs` | Anonymous | Send a log entry |
| GET | `/api/client/images` | User | Images the caller may install |
| POST | `/api/client/name` | User | Generate a computer name |
| POST | `/api/client/drivers/query` | User | Driver packages for this hardware |
| POST | `/api/client/multicast/{imageId}/join` | User | Join the multicast of an image |
| POST | `/api/client/multicast/{id}/clients/{clientId}` | User | Report multicast progress or leave |
| GET | `/openapi/v1.json` | Anonymous | OpenAPI document |
