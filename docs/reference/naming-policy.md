# Naming policy

The naming policy generates computer names for devices approved without a name and for `POST /api/client/name`. It uses the WDS syntax. It is set in `namingPolicy` of the [server settings](server-settings.md). Source: `src/Sds.Core/Computers/NamingPolicy.cs`.

## Tokens

| Token | Value |
|---|---|
| `%Username` | User name, without `DOMAIN\` prefix or `@domain` suffix |
| `%First` | First name |
| `%Last` | Last name |
| `%MAC` | MAC address, 12 hex digits |
| `%n<Variable>` | The variable limited to its first `n` characters, for example `%61Username`, `%3First` |
| `%#` | Incrementing number: 1, 2, 3... |
| `%n#` | Incrementing number (the digit count is accepted for WDS compatibility; no padding) |
| `%0n#` | Incrementing number padded with zeros to `n` digits: `%03#` gives `001` |
| `%%` | A literal `%` |
| anything else | Literal text |

Token names are case insensitive. An unknown token, an empty policy, or a policy without any variable or `%#` is rejected with `400` when the settings are saved.

## Rules

- Only letters, digits and hyphens are kept, from variable values and literal text alike. The result is upper cased and leading and trailing hyphens are removed.
- Names are limited to 15 characters (NetBIOS). When the result is too long, text before the number is shortened from the right so the number stays intact.
- The number starts at 1 and increases until the name is free in SDS (and in Active Directory when AD integration is on).
- Without `%#`, a name that is already taken gives `409`.
- A policy that produces an empty name (for example `%Username` with no user) gives `400`.

## Examples

| Policy | Values | Result |
|---|---|---|
| `%61Username%#` (default) | user `CONTOSO\ada.lovelace` | `ADALOVELACE1`, then `ADALOVELACE2`... |
| `%First%Last%#` | Ada, Lovelace | `ADALOVELACE1` |
| `LAB-%03#` | | `LAB-001`, `LAB-002`... |
| `%3First%5Last-%02#` | Ada, Lovelace | `ADALOVEL-01` |
| `PC-%MAC` | MAC `00155D010203` | `PC-00155D010203` |
