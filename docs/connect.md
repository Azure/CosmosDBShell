# Connection Guide

The `connect` command supports multiple authentication methods. The shell automatically selects the appropriate credential type based on the arguments provided.

## Credential Decision Tree

The credential type is determined by the first matching rule (top-to-bottom):

| Priority | Condition | Credential Used |
|----------|-----------|-----------------|
| 1 | Endpoint is `localhost` or `127.0.0.1` | Emulator (well-known key) |
| 2 | Connection string has `AccountKey`, or `COSMOS_SHELL_CREDENTIAL` env provides a key | Account key |
| 3 | `--connect-vscode-credential` flag provided | `VisualStudioCodeCredential` (falls back to next step) |
| 4 | `COSMOS_SHELL_TOKEN` env var is set | Static access token |
| 5 | `--managed-identity` option provided | `ManagedIdentityCredential` |
| 6 | `--tenant` or `--hint` option provided | `InteractiveBrowserCredential` (with `DeviceCodeCredential` fallback) |
| 7 | Endpoint only (no additional arguments) | `DefaultAzureCredential` |

The `--authority-host` option is passed through to whichever credential is created (priorities 3-6). It does not affect which credential type is selected.

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

## COSMOS_SHELL_TOKEN Environment Variable

This environment variable provides a pre-obtained Entra ID access token (JWT) for authentication. This is intended for single-shot command execution where an external process handles token acquisition.

```bash
# Obtain a token with the Cosmos DB RBAC scope, then pass it
export COSMOS_SHELL_TOKEN=$(az account get-access-token --resource https://<account>.documents.azure.com --query accessToken -o tsv)
cosmos-shell --connect https://myaccount.documents.azure.com:443/ -c "cd mydb/mycont; ls -m 5"
```

The token must be issued for the Cosmos DB RBAC scope (`https://<account>.documents.azure.com/.default`). The external process is responsible for obtaining a valid token with the correct scope and permissions.

When `COSMOS_SHELL_TOKEN` is set, it takes priority over managed identity, interactive browser, and `DefaultAzureCredential` — but account keys (from the connection string or `COSMOS_SHELL_CREDENTIAL`) still take priority over it.

> **Security note:** Environment variable values may be visible to other processes on the system. This is standard practice for CI/CD token passing, but avoid setting `COSMOS_SHELL_TOKEN` in shared or untrusted environments.

## CLI Startup Options

All connect options are also available as CLI startup arguments:

```bash
cosmos-shell --connect https://myaccount.documents.azure.com:443/ --connect-tenant=<tenant-id>
cosmos-shell --connect https://myaccount.documents.azure.com:443/ --connect-managed-identity=<client-id>
cosmos-shell --connect https://localhost:8081
```

The hidden `--connect-vscode-credential` flag enables `VisualStudioCodeCredential` authentication via the system broker. This is used by the VS Code extension and requires the Azure Resources extension to be signed in. If the credential is unavailable, the shell falls back to `COSMOS_SHELL_TOKEN` and then to subsequent credential steps.

## Connection Info

Run `connect` with no arguments to display the current connection info:

```
> connect
Connection Information
 Account     myaccount
 Endpoint    https://myaccount.documents.azure.com:443/
 Mode        Direct
 ...
```
