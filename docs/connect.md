# Connection Guide

The `connect` command supports multiple authentication methods. The shell automatically selects the appropriate credential type based on the arguments provided.

## Credential Decision Tree

The credential type is determined by the first matching rule (top-to-bottom):

| Priority | Condition | Credential Used |
|----------|-----------|-----------------|
| 1 | Endpoint is `localhost` or `127.0.0.1` | Emulator (well-known key) |
| 2 | Connection string has `AccountKey`, or `COSMOS_SHELL_CREDENTIAL` env provides a key | Account key |
| 3 | `--managed-identity` option provided | `ManagedIdentityCredential` |
| 4 | `--tenant` or `--hint` option provided | `InteractiveBrowserCredential` (with `DeviceCodeCredential` fallback) |
| 5 | Endpoint only (no additional arguments) | `DefaultAzureCredential` |

The `--authority-host` option is passed through to whichever credential is created (priorities 3-5). It does not affect which credential type is selected.

## Examples

### Account Key

```bash
# Full connection string
connect "AccountEndpoint=https://myaccount.documents.azure.com:443/;AccountKey=mykey;"

# Key via environment variable
export COSMOS_SHELL_CREDENTIAL="myaccountkey"
connect https://myaccount.documents.azure.com:443/
```

### Emulator

```bash
# Plain URL — automatically uses well-known emulator key + gateway mode
connect https://localhost:8081
```

### Managed Identity (User-Assigned)

```bash
connect https://myaccount.documents.azure.com:443/ --managed-identity=<client-id>
```

### Managed Identity (System-Assigned)

For system-assigned managed identity, no `--managed-identity` argument is needed. Use the endpoint-only form — `DefaultAzureCredential` includes `ManagedIdentityCredential` in its chain automatically:

```bash
connect https://myaccount.documents.azure.com:443/
```

### Entra ID (Interactive Browser)

```bash
# With tenant ID
connect https://myaccount.documents.azure.com:443/ --tenant=<tenant-id>

# With login hint
connect https://myaccount.documents.azure.com:443/ --hint=user@contoso.com

# With both
connect https://myaccount.documents.azure.com:443/ --tenant=<tenant-id> --hint=user@contoso.com
```

If browser authentication fails, the shell automatically falls back to device code authentication.

### DefaultAzureCredential

When only an endpoint is provided with no additional arguments, the shell uses `DefaultAzureCredential`. This tries credentials in the following order: Environment, Workload Identity, Managed Identity, Visual Studio, VS Code, Azure CLI, Azure PowerShell, Azure Developer CLI, Interactive Browser.

```bash
connect https://myaccount.documents.azure.com:443/
```

### Custom Authority Host

For sovereign clouds or custom Entra environments, use `--authority-host`:

```bash
connect https://myaccount.documents.azure.com:443/ --authority-host=https://login.microsoftonline.us/
```

## COSMOS_SHELL_CREDENTIAL Environment Variable

This environment variable provides an account key for authentication. It supports two formats:

```bash
# With key= prefix
export COSMOS_SHELL_CREDENTIAL="key=myaccountkey"

# Raw key (no prefix)
export COSMOS_SHELL_CREDENTIAL="myaccountkey"
```

If the connection string already contains an `AccountKey`, the environment variable is ignored.

## CLI Startup Options

All connect options are also available as CLI startup arguments:

```bash
cosmos-shell --connect https://myaccount.documents.azure.com:443/ --connect-tenant=<tenant-id>
cosmos-shell --connect https://myaccount.documents.azure.com:443/ --connect-managed-identity=<client-id>
cosmos-shell --connect https://localhost:8081
```

## Connection Info

Run `connect` with no arguments to display the current connection info, including the credential type used:

```
> connect
Connection Information
 Account     myaccount
 Endpoint    https://myaccount.documents.azure.com:443/
 Mode        Direct
 Credential  DefaultAzureCredential
 ...
```
