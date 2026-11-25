# Conventions

## JSON

- Request and response bodies are JSON (`application/json`) unless the endpoint says otherwise. The exceptions are the unattend endpoints (raw XML), uploads (raw bytes) and the export download (raw bytes).
- Property names are **camelCase**: the model `ImageDto.GroupName` is `groupName` on the wire. Property names in requests are matched case insensitively.
- Enums are **strings** (`"Ready"`, `"KnownOnly"`), not numbers. See [Enumerations](reference/enums.md).
- Properties whose value is `null` are **omitted** from responses. Treat a missing property as `null`.
- Dates are ISO 8601. Fields ending in `Utc` are UTC.
- Ids are integers for images, image groups, computers and multicast transmissions; 64 bit integers for pending devices and client logs; GUIDs for driver packages and jobs. Image groups and driver groups are addressed by **name** in URLs.

Encode names in URLs: `GET /api/image-groups/Windows%2011`.

## Partial and full updates

- `PATCH` endpoints (image groups, images, drivers) change only the fields that are present and not `null`.
- `PUT` endpoints (computers, driver groups, server settings) replace the whole object. Fields left out take their default value. Read the object first, change it, and send it back.

## Identifiers

The server normalises the identifiers it receives:

| Kind | Accepted input | Stored and returned as |
|---|---|---|
| MAC address | 12 hex digits with optional `:`, `-`, `.` or space separators, any case | `AABBCCDDEEFF` |
| SMBIOS GUID | Any GUID format | GUID. All zero and all `F` values are treated as "no GUID", like WDS |
| Architecture | `x86`, `i386`, `ia32` → `x86`; `x64`, `amd64`, `x86_64` → `amd64`; `arm64`, `aarch64` → `arm64`; `arm`; `ia64` | Lower case canonical name |
| Computer name | 1 to 15 letters, digits or hyphens, not only digits | Upper case |
| Group and file names | Valid Windows file names, not reserved device names (`CON`, `NUL`...) | As given |

Malformed values give `400`.

## Errors

Errors use RFC 7807 Problem Details (`application/problem+json`):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Conflict",
  "status": 409,
  "detail": "Image group 'Windows 11' already exists."
}
```

| Status | Title | When |
|---|---|---|
| 400 | Invalid request | Validation failed (missing field, malformed MAC, unknown architecture, bad XML...). Model binding errors (wrong JSON type) also give `400` with an `errors` object. |
| 401 | | No or invalid Windows credentials |
| 403 | Access denied | Not in the required role, or the server was denied access to a file |
| 404 | Not found | The object does not exist. XML endpoints answer `404` with no body when no XML is set. |
| 409 | Conflict | Duplicate name, MAC or GUID; object in the wrong state (for example a pending device already approved, a group that still has images) |
| 500 | Imaging error | `wimgapi.dll` failed; `detail` holds the Win32 message |
| 500 | Server error | Unexpected error; `detail` is generic and the server log has the exception |

The `detail` of `400`, `403`, `404`, `409` and imaging errors is meant to be shown to the administrator.

Errors that happen inside a background job are not HTTP errors: the job ends with `status: "Failed"` and an `error` message. See [Jobs](jobs-and-uploads.md).

## Success codes

| Status | When |
|---|---|
| 200 | The body holds the result |
| 201 | Created. The `Location` header points to the new object. |
| 202 | A background job was queued. The body is the [Job](reference/models.md#job) and `Location` is `/api/jobs/{id}`. |
| 204 | Done, no body (most deletes, XML uploads) |

## Logs

The server logs to the console, to `%ProgramData%\SDS\Logs\sds-<date>.log` (30 days kept) and, for warnings and errors, to the Windows Event Log under the source `SDS`.
