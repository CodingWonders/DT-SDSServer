# Configuration

Infrastructure configuration lives in `appsettings.json` next to the server executable (`src/Sds.Server/appsettings.json` in the source tree), under the `Sds` section. It is read at startup; restart the server after a change. Run time policy (response policy, naming, logging...) is in the database instead: see [Server settings](server-settings.md).

Source: `src/Sds.Core/SdsOptions.cs`, `src/Sds.DirectoryServices/ActiveDirectoryComputerDirectory.cs`, `src/Sds.Server/Program.cs`.

Any value can also be set with environment variables (`Sds__Volume=E:`) or command line arguments (`--Sds:Volume=E:`), as usual for ASP.NET Core.

## Top level

| Key | Default | Description |
|---|---|---|
| `Urls` | `http://*:8350` | Addresses the server listens on |
| `AllowedHosts` | `*` | Host filtering |
| `Serilog` | | Log levels (Serilog configuration) |

## Sds

| Key | Default | Description |
|---|---|---|
| `Volume` | `C:` | Volume of the deployment root. The root is always `<Volume>\SDSREMINST`. |
| `RootOverride` | | Full root path. For tests and development only. |
| `ImagesShareName` | `images` | SMB share name of `IMAGES` |
| `DriversShareName` | `drivers` | SMB share name of `DRIVERS` |
| `CreateShares` | true | Create the shares at startup when missing. Existing shares are never changed. |
| `ApplyDefaultAcls` | true | Apply the default NTFS permissions when the root folder is created |
| `CreateLocalGroups` | true | Create the security groups at startup when missing |
| `CreateFirewallRule` | true | Create an inbound Windows Firewall rule for the API port(s) at startup when missing. Existing rules are never changed. |
| `FirewallRuleName` | `SDS Server (TCP-In)` | Name of that rule |
| `DataDirectory` | `%ProgramData%\SDS` | Database (`sds.db`), `Logs` and `Staging` |
| `Compression` | `Lzx` | Compression of new `Res.RWM` and metadata WIMs (`None`, `Xpress`, `Lzx`, `Lzms`) |
| `MaxConcurrentJobs` | 2 | Background jobs running at the same time |
| `StagingRetentionHours` | 24 | Uploads and staged exports are deleted after this many hours |
| `AdministratorsGroup` | `SDS Administrators` | Group with the Admin role |
| `OperatorsGroup` | `SDS Operators` | Group with the Reader role |
| `ExtraAdministratorGroups` | `[]` | More groups (names or SIDs) with the Admin role |
| `ExtraOperatorGroups` | `[]` | More groups (names or SIDs) with the Reader role |
| `PublicServerName` | machine DNS name | Host name used in the UNC paths returned by the API |
| `EventLog` | true | Write warnings and errors to the Windows Event Log (source `SDS`) |
| `CorsOrigins` | `["http://localhost:8351"]` | More browser origins allowed to call the API with credentials (the console on this machine is always allowed, see `ConsolePort`) |
| `ConsolePort` | 8351 | Port of the Web Console when it runs on the same machine. Its origin is allowed under whatever host name the browser used to reach the API (localhost, IP address, machine name). 0 turns this off. |
| `PasswordSignIn` | true | Allow sign-in with a Windows user name and password (`POST /api/auth/login`). See [Password sign-in](../authentication.md#password-sign-in). |
| `AllowPasswordSignInOverHttp` | false | Accept password sign-in over plain HTTP from other machines. Leave off: passwords would cross the network in clear text. Loopback is always accepted. |
| `SessionMinutes` | 480 | Minutes without use before a password session ends |
| `HttpsPort` | 8352 | Port of the HTTPS endpoint added automatically when the certificate `HttpsCertificateSubject` is installed. 0 turns it off. Ignored when `Kestrel:Endpoints` is configured. See [HTTPS](#https). |
| `HttpsCertificateSubject` | `SDS Server HTTPS` | Common name of that certificate, looked up in `LocalMachine\My`, then `CurrentUser\My` |

`BUILTIN\Administrators` always has the Admin role, whatever the configuration.

The firewall rule allows inbound TCP on every port the server listens on from the network (`Urls`, `--urls`, `ASPNETCORE_URLS`, `Kestrel:Endpoints`), for all profiles. Loopback addresses such as `http://localhost:8350` need no rule and are skipped. When a rule with the same name exists, the server only updates its ports, and only if SDS created it (rule group `DISMTools System Deployment Services`); profiles and remote addresses you narrowed are kept. A rule SDS did not create is left as it is, with a warning if its ports do not match. Creating the rule needs administrator rights; without them the server logs a warning and starts anyway.

## HTTPS

Password sign-in needs HTTPS when the browser is on another machine.

### Automatic (self-signed certificate)

Run `New-SdsCertificate.ps1` (next to `SdsServer.exe`, source in `scripts/`) as administrator:

```powershell
.\New-SdsCertificate.ps1 -Restart
```

The script creates a self-signed certificate `CN=SDS Server HTTPS` for the computer name, `localhost` and the machine's IP addresses, and exports its public part (`SdsServer.cer`). The certificate goes into the machine store (`LocalMachine\My`) by default, which the service and an elevated server read, and is valid for 2 years from the moment it is created; run the script again to renew it. Use `-StoreLocation CurrentUser` only when the server runs unelevated under your account. `-Restart` restarts the `SDS` service.

Nothing has to be edited. At startup the server looks for that certificate in `LocalMachine\My`, then `CurrentUser\My`, and when it finds a valid one whose private key it can open, it:

- adds an HTTPS endpoint on `HttpsPort` (8352), limited to TLS 1.2 and 1.3, next to its HTTP addresses,
- adds the port to its firewall rule,
- logs `HTTPS is on: ...` (or why it is off).

The Web Console on the same machine uses the same certificate: its port 8351 then answers both `http://` and `https://` (it tells them apart by the first byte of each connection), redirects `http://` to `https://` on the same address, and calls the API over HTTPS on 8352 (`SdsApiScheme` `auto`). Its `Https` setting (default `true`) turns this off. Without the certificate the console stays HTTP only.

Browsers do not trust a self-signed certificate, and a page cannot show the certificate prompt for its API calls. The Web Console takes care of it from the sign-in page: when it cannot reach the API over HTTPS, it sends the browser to the API's `GET /api/trust` once, the browser shows its certificate warning there, and after the user accepts, the API sends the browser straight back to the console. `/api/trust` only redirects to pages allowed by the CORS policy (`CorsOrigins`, or the console on this server). To avoid the warning altogether, import `SdsServer.cer` on the client machines. Run the script again when the server's IP address changes.

### Your own certificate

With a certificate from your certificate authority, configure the endpoints yourself. Explicit `Kestrel:Endpoints` turn the automatic endpoint off, and they replace `Urls`, so list the HTTP endpoint too:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://*:8350" },
      "Https": {
        "Url": "https://*:8352",
        "SslProtocols": [ "Tls12", "Tls13" ],
        "Certificate": { "Subject": "sds01.contoso.com", "Store": "My", "Location": "LocalMachine" }
      }
    }
  }
}
```

`SslProtocols` limits the endpoint to TLS 1.2 and 1.3. Without it Kestrel accepts whatever Windows allows, which can include TLS 1.0 and 1.1. The firewall rule covers every configured port.

A Web Console on the same machine finds the HTTPS port by itself when it is 8352 (or set `SdsApiHttpsPort`). A console elsewhere needs `SdsApiUrl` (`https://sds01.contoso.com:8352`), and the server must list its origin in `CorsOrigins`.

## Active Directory

`Sds:ActiveDirectory` enables mirroring known computers into Active Directory the way WDS prestages devices: a computer object with `sAMAccountName` `NAME$`, `netbootGUID` holding the SMBIOS GUID (or the MAC padded to a GUID), `netbootMachineFilePath` holding the referral server, and optional join rights for a user.

| Key | Default | Description |
|---|---|---|
| `Enabled` | false | Turns the provider on |
| `LdapPath` | | Domain or domain controller, for example `LDAP://dc01.contoso.com/DC=contoso,DC=com`. Empty binds to the server's domain. |
| `DefaultOrganizationalUnit` | | OU for computers without one. Empty uses `CN=Computers`. |
| `Username` | | Account for the LDAP bind. Empty uses the service account. |
| `Password` | | Password of `Username` |

With AD on:

- creating, updating and deleting computers writes to AD ([computers](../api/computers.md)),
- a registering device that exists only in AD is imported as a known computer,
- generated names are checked against AD for uniqueness,
- `GET /api/server` reports `activeDirectory: true`.

## Example

```json
{
  "Urls": "http://*:8350",
  "Sds": {
    "Volume": "E:",
    "PublicServerName": "sds01.contoso.com",
    "Compression": "Lzx",
    "MaxConcurrentJobs": 2,
    "ExtraAdministratorGroups": [ "CONTOSO\\Deployment Admins" ],
    "ExtraOperatorGroups": [ "S-1-5-21-1004336348-1177238915-682003330-1105" ],
    "CorsOrigins": [ "https://sdsconsole.contoso.com" ],
    "ActiveDirectory": {
      "Enabled": true,
      "DefaultOrganizationalUnit": "OU=Deploy,DC=contoso,DC=com"
    }
  }
}
```

## Files on disk

```
<Volume>\SDSREMINST\                       not shared
  PXECORE\                                 SharpPXE and the boot image (not managed by SDS)
  IMAGES\                                  shared as \\<host>\images
  IMAGES\<Group>\Res.RWM                   single instance resources of the group's images
  IMAGES\<Group>\<image>.wim               metadata only WIM per image
  IMAGES\<Group>\<image>\Unattend\ImageUnattend.xml
  DRIVERS\                                 shared as \\<host>\drivers
  DRIVERS\<drvname>_<arch>_{GUID}\         one folder per driver package
%ProgramData%\SDS\
  sds.db                                   database
  Logs\                                    server logs
  Staging\uploads, Staging\exports         temporary files
```
