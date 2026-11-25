# Authentication and authorization

## Authentication

SDS uses Windows Negotiate authentication (Kerberos, falling back to NTLM). There are no API keys or tokens: callers send their Windows credentials.

- PowerShell: `-UseDefaultCredentials` (or `-Credential`).
- curl: `--negotiate -u :`.
- .NET: `new HttpClientHandler { UseDefaultCredentials = true }`.
- Browsers: the server must be in the intranet zone (or the browser must be configured to send credentials to it) and the request must use `credentials: "include"`.

An unauthenticated request to a protected endpoint gets `401` with `WWW-Authenticate: Negotiate`.

## Roles

Authorization is decided from the group SIDs in the caller's Windows token.

| Role | Policy name | Who |
|---|---|---|
| Admin | `SdsAdmin` | `BUILTIN\Administrators`, the `AdministratorsGroup` (default `SDS Administrators`), and every group in `ExtraAdministratorGroups` |
| Reader | `SdsReader` | Everyone in Admin, plus the `OperatorsGroup` (default `SDS Operators`) and every group in `ExtraOperatorGroups` |
| User | (authenticated) | Any authenticated Windows account |
| Anonymous | (none) | No credentials |

Rules:

- Every management controller requires **Reader**. Every call that changes something (POST, PUT, PATCH, DELETE) requires **Admin**.
- `POST /api/uploads`, `DELETE /api/uploads/{id}`, `POST /api/images/inspect` and `GET /api/jobs/{id}/file` require **Admin** although some are not writes.
- Any endpoint without an explicit rule requires an authenticated caller (fallback policy).
- Group names in configuration are resolved to SIDs once, when the server starts. Raw SIDs (`S-1-5-21-...`) are accepted too, which is useful for domain groups. A name that cannot be resolved is logged as a warning and ignored. Restart the server after changing group membership in configuration.
- A caller who is authenticated but not in the required role gets `403`.

On a domain controller there is no local SAM, so SDS creates `SDS Administrators` and `SDS Operators` as domain local groups in `CN=Users` of the domain.

## Deployment client endpoints

The deployment client endpoints under `/api/client` follow WDS:

| Endpoint | Access |
|---|---|
| `POST /api/client/register` | Anonymous |
| `GET /api/client/requests/{id}` | Anonymous |
| `GET /api/client/unattend/{architecture}` | Anonymous |
| `POST /api/client/logs` | Anonymous |
| `GET /api/client/images` | User |
| `POST /api/client/name` | User |
| `POST /api/client/drivers/query` | User |
| `POST /api/client/multicast/...` | User |

"User" is the person running the deployment: their account is used for the image user filter (below) and for naming.

`GET /openapi/v1.json` is anonymous.

## Image user filter

As in WDS, the NTFS permissions of an install image file decide who sees the image. `GET /api/client/images` only returns images whose metadata WIM the caller can read. The permissions are managed through:

- `GET/PUT /api/images/{id}/acl` (one image), and
- `GET/PUT /api/image-groups/{name}/acl` (the group folder; inherited by its images unless an image breaks inheritance).

See [AclDto](reference/models.md#acldto).

Management endpoints (`GET /api/images`, etc.) are not filtered: Readers see every image.

## Share permissions

On start SDS creates the `images` and `drivers` shares when they are missing, with the share permissions WDS gives `REMINST` (SYSTEM and Administrators full, Authenticated Users read) plus `SDS Administrators` full. An existing share is never changed. The `SDSREMINST` root and `PXECORE` are never shared.

UNC paths returned by the API (for example `\\<host>\images\<Group>\install.wim`) point to these shares. The host part is `PublicServerName` from configuration, or the machine DNS name.

## CORS

Browser front ends on another origin (such as the Web Console on port `8351`) call the API cross origin with credentials. Only origins listed in `Sds:CorsOrigins` are allowed; wildcards are not possible with credentials. All methods and headers are allowed and the `Location` and `Content-Disposition` headers are exposed.

CORS runs before authentication, so preflight `OPTIONS` requests (which browsers send without credentials) are answered instead of being challenged.
