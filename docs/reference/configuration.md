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
| `CorsOrigins` | `["http://localhost:8351"]` | Browser origins allowed to call the API with credentials |

`BUILTIN\Administrators` always has the Admin role, whatever the configuration.

The firewall rule allows inbound TCP on every port the server listens on from the network (`Urls`, `--urls`, `ASPNETCORE_URLS`, `Kestrel:Endpoints`), for all profiles. Loopback addresses such as `http://localhost:8350` need no rule and are skipped. A rule with the same name is left as it is, so you can narrow it (profiles, remote addresses); if its ports no longer match, the server logs a warning. Creating the rule needs administrator rights; without them the server logs a warning and starts anyway.

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
