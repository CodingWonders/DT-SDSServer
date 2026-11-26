# Authentication and authorization

## Authentication

SDS uses Windows accounts, in one of two ways:

- **Windows sign-in** (all clients): Negotiate authentication (Kerberos, falling back to NTLM). There are no API keys or tokens: callers send their Windows credentials.
- **Password sign-in** (browser front ends): `POST /api/auth/login` checks a Windows user name and password once and issues a session cookie. Meant for browsers that do not send Windows credentials. See [Password sign-in](#password-sign-in).

Windows sign-in from the usual clients:

- PowerShell: `-UseDefaultCredentials` (or `-Credential`).
- curl: `--negotiate -u :`.
- .NET: `new HttpClientHandler { UseDefaultCredentials = true }`.
- Browsers: the request must use `credentials: "include"`, and the browser must be allowed to send Windows credentials to the server. Edge and Chrome on Windows do so for servers in the intranet zone (Chrome also honors the `AuthServerAllowlist` policy). Firefox needs the server in `network.negotiate-auth.trusted-uris` (Kerberos) and `network.automatic-ntlm-auth.trusted-uris` (NTLM), set in `about:config` or through its `Authentication` enterprise policy; for a short host name also set `network.automatic-ntlm-auth.allow-non-fqdn`. Otherwise use password sign-in.

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

## Signing in

Management front ends sign the user in before anything else and refuse to continue unless the account is a **Reader** (or **Admin**). The Web Console starts on a sign-in screen that offers both ways.

### GET /api/auth/me

Returns the caller's account, SDS role and sign-in method. **User** (any authenticated account, by either way), so it answers `200` for accounts that are authenticated but not authorized, and `401` without credentials.

Response `200`:

| Field | Type | Description |
|---|---|---|
| `signedIn` | bool | Always true here |
| `name` | string | Windows account name, for example `CONTOSO\alice` |
| `role` | string | `Admin`, `Reader` or `None` |
| `isAdmin` | bool | True when the caller satisfies `SdsAdmin` |
| `isReader` | bool | True when the caller satisfies `SdsReader` (always true for admins) |
| `method` | string | `Windows` or `Password` |

### Password sign-in

For browsers that do not send Windows credentials (for example Firefox without configuration). The account is still a Windows account: the server checks the password with `LogonUser` (network logon, the same kind of logon Negotiate produces), copies the groups of the resulting token into the session, and applies the same roles.

| Method | Path | Access | Description |
|---|---|---|---|
| POST | `/api/auth/login` | Anonymous | Body `{ "userName": "...", "password": "..." }`. Starts a session for a Reader or Admin account. |
| GET | `/api/auth/session` | Anonymous | The current password session in the same shape as `GET /api/auth/me`, or `{ "signedIn": false }`. Lets a front end resume after a reload without starting a Windows sign-in. |
| POST | `/api/auth/logout` | Anonymous | Ends the password session (`204`). |

`userName` is `DOMAIN\user`, `user@domain`, or a bare name for a local account of the server.

`POST /api/auth/login` answers:

| Status | When |
|---|---|
| `200` | Signed in. Body as `GET /api/auth/me`; the response sets the `sds.session` cookie. |
| `400` | Missing user name or password, missing `X-SDS-Request` header, or plain HTTP from another machine (see below) |
| `401` | Wrong user name or password, or the account cannot sign in (disabled, locked out, expired). The reason is logged on the server only. |
| `403` | Valid account without the Reader or Admin role. No session is created. |
| `404` | Password sign-in is turned off (`Sds:PasswordSignIn`) |
| `429` | More than 10 attempts from the same address in 5 minutes. The Windows account lockout policy applies too. |

Rules:

- **HTTPS.** The password and the cookie must not cross the network in clear text, so password sign-in is refused over plain HTTP unless the request comes from the server itself (loopback) or `Sds:AllowPasswordSignInOverHttp` is set. Configure an HTTPS endpoint (see [Configuration](reference/configuration.md#https)).
- **Session.** The `sds.session` cookie is HttpOnly. It ends after `Sds:SessionMinutes` (default 480) without use, on logout, or when the server's data protection keys change. Group membership is read at sign-in; sign in again after a change. Over HTTPS the cookie is `SameSite=None; Secure` so a console on another host works; over HTTP it is `SameSite=Lax`, which still covers a console on the same host as the API (any port).
- **CSRF guard.** A request authenticated by the cookie that changes something (anything but GET, HEAD, OPTIONS) must carry the `X-SDS-Request` header, and so must `POST /api/auth/login`. A browser only sends a custom header cross origin after a CORS preflight, which only the origins in `Sds:CorsOrigins` pass. Without the header the request is treated as anonymous (`401`).
- **Which scheme.** A request that carries the cookie and no `Authorization` header is authenticated by the cookie; any other request by Negotiate.
- **User filter.** `GET /api/client/images` impersonates the caller's Windows token to apply the image user filter, which a password session does not have. It answers `403` to password sessions.

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
