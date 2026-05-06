# Connection Guide

The `connect` command and the `--connect` startup option support multiple authentication methods. The shell automatically selects the appropriate credential type based on the arguments provided.

## Credential Decision Tree

The credential type is determined by the first matching rule (top-to-bottom):

| Priority | Condition | Credential Used |
| -------- | --------- | --------------- |
| 1 | Endpoint is `localhost` or `127.0.0.1` | Emulator (well-known key) |
| 2 | Connection string has `AccountKey`, or `COSMOSDB_SHELL_ACCOUNT_KEY` env provides a key | Account key |
| 3 | `--connect-vscode-credential` startup flag provided (startup only) | `VisualStudioCodeCredential` (falls back to next step) |
| 4 | `COSMOSDB_SHELL_TOKEN` env var is set | Static access token |
| 5 | `--managed-identity` option provided | `ManagedIdentityCredential` |
| 6 | `--tenant` or `--hint` option provided | `InteractiveBrowserCredential` (with `DeviceCodeCredential` fallback) |
| 7 | Endpoint only (no additional arguments) | `DefaultAzureCredential` |

> **Note:** Step 3 (`--connect-vscode-credential`) is only available as a CLI startup option, not as an argument to the interactive `connect` command.

The `--authority-host` option is passed through to whichever credential is created (priorities 3-6). It does not affect which credential type is selected.

## Azure Resource Manager Context

Database and container resource operations (listing, navigating to, creating, deleting, and reading settings for databases and containers) prefer Azure Resource Manager (ARM) when an ARM context is attached. Item operations always use the Cosmos DB data plane.

ARM context is attached only for Entra ID credential flows: `VisualStudioCodeCredential`, `ManagedIdentityCredential`, `InteractiveBrowserCredential`, `DeviceCodeCredential`, and `DefaultAzureCredential`. Account-key connections, emulator connections, and `COSMOSDB_SHELL_TOKEN` connections do not attach ARM context, so resource operations fall back to the Cosmos DB data plane.

When ARM context is attached, the shell can discover the ARM account by matching the connected data-plane endpoint across accessible subscriptions. For deterministic startup, especially in CI/CD or multi-subscription environments, provide the coordinates explicitly:

```bash
connect https://myaccount.documents.azure.com:443/ --tenant=<tenant-id> --subscription=<subscription-id> --resource-group=<resource-group> --account=<account-name>
cosmosdbshell --connect https://myaccount.documents.azure.com:443/ --connect-tenant=<tenant-id> --connect-subscription=<subscription-id> --connect-resource-group=<resource-group> --connect-account=<account-name>
```

When ARM is in use, the identity needs data-plane RBAC for item operations and Azure management-plane permissions for database/container resources, such as Cosmos DB Operator or equivalent scoped permissions on the account. When falling back to the data plane (account-key, emulator, static token), the connection's existing data-plane authority is used for all commands.

## Examples

### Account Key

```bash
# Full connection string
connect "AccountEndpoint=https://myaccount.documents.azure.com:443/;AccountKey=mykey;"

# Key via environment variable
export COSMOSDB_SHELL_ACCOUNT_KEY="myaccountkey"
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

## COSMOSDB_SHELL_ACCOUNT_KEY Environment Variable

This environment variable provides an account key for authentication:

```bash
export COSMOSDB_SHELL_ACCOUNT_KEY="myaccountkey"
```

If the connection string already contains an `AccountKey`, the environment variable is ignored.

## COSMOSDB_SHELL_TOKEN Environment Variable

This environment variable provides a pre-obtained Entra ID access token (JWT) for authentication. This is intended for single-shot command execution where an external process handles token acquisition.

```bash
# Obtain a token with the Cosmos DB RBAC scope, then pass it
export COSMOSDB_SHELL_TOKEN=$(az account get-access-token --resource https://<account>.documents.azure.com --query accessToken -o tsv)
cosmosdbshell --connect https://myaccount.documents.azure.com:443/ -c "cd mydb/mycont; ls -m 5"
```

The token must be issued for the Cosmos DB RBAC scope (`https://<account>.documents.azure.com/.default`). The external process is responsible for obtaining a valid token with the correct scope and permissions.

When `COSMOSDB_SHELL_TOKEN` is set, it takes priority over managed identity, interactive browser, and `DefaultAzureCredential` — but account keys (from the connection string or `COSMOSDB_SHELL_ACCOUNT_KEY`) still take priority over it.

> **Security note:** Environment variable values may be visible to other processes on the system. This is standard practice for CI/CD token passing, but avoid setting `COSMOSDB_SHELL_TOKEN` in shared or untrusted environments.

## CLI Startup Options

All connect options are also available as CLI startup arguments:

```bash
cosmosdbshell --connect https://myaccount.documents.azure.com:443/ --connect-tenant=<tenant-id>
cosmosdbshell --connect https://myaccount.documents.azure.com:443/ --connect-managed-identity=<client-id>
cosmosdbshell --connect https://myaccount.documents.azure.com:443/ --connect-subscription=<subscription-id> --connect-resource-group=<resource-group> --connect-account=<account-name>
cosmosdbshell --connect https://localhost:8081
```

The hidden `--connect-vscode-credential` flag enables `VisualStudioCodeCredential` authentication via the system broker. This is used by the VS Code extension and requires the Azure Resources extension to be signed in. If the credential is unavailable, the shell falls back to `COSMOSDB_SHELL_TOKEN` and then to subsequent credential steps.

## Connection Info

Run `connect` with no arguments to display the current connection info:

```text
> connect
Connection Information
 Account     myaccount
 Endpoint    https://myaccount.documents.azure.com:443/
 Mode        Direct
 ...
```

## Security Considerations

Cosmos Shell is a developer and CI/CD tool, and all supported authentication methods are valid for those use cases. The notes below help you choose the right method for your environment and understand the tradeoffs.

### Account Keys

Account keys (including connection strings that contain an `AccountKey`) grant **full, unrestricted access** to the entire Cosmos DB account. This includes all databases, containers, and operations — there is no way to scope a key to a subset of resources or to read-only access.

Keys are static shared secrets. If a key is leaked, it remains valid until you manually rotate it on the account. Anyone with the key can read, write, and delete any data.

For production and security-sensitive workloads, Microsoft recommends [disabling key-based authentication](https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-connect-role-based-access-control#disable-key-based-authentication) (`disableLocalAuth=true`) and using Entra ID with [data-plane RBAC](https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-connect-role-based-access-control#grant-data-plane-role-based-access) instead. RBAC allows least-privilege scoping — for example, granting read-only access to a single container.

Account keys are acceptable when:

- Connecting to the **local emulator** (which uses a well-known key by design).
- Running in a **CI/CD pipeline** where the key is stored in a secure secret store (e.g., Azure Key Vault, GitHub Actions secrets) and never written to logs.
- Doing **local development** against a non-production account.

### DefaultAzureCredential Security

`DefaultAzureCredential` is the most convenient option for development — it automatically tries multiple credential sources (Azure CLI, VS Code, managed identity, and others) until one succeeds. However, this convenience introduces unpredictability: you cannot guarantee which credential in the chain will be used at runtime.

In shared or production environments, this can lead to subtle problems. For example, if a developer runs `az login` on a VM that normally authenticates via managed identity, `DefaultAzureCredential` may silently fall back to the CLI credential with different permissions. Microsoft recommends [using a deterministic credential](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/best-practices#use-deterministic-credentials-in-production-environments) (such as `ManagedIdentityCredential`) in production instead of relying on the automatic chain.

For Cosmos Shell usage:

- **Local development**: `DefaultAzureCredential` (endpoint-only connection) is a good default. It picks up your Azure CLI or VS Code session automatically.
- **CI/CD pipelines**: Prefer explicit credential types — `--managed-identity` for Azure-hosted runners, or `COSMOSDB_SHELL_TOKEN` with a pre-obtained token.
- **Shared VMs or containers**: Use `--managed-identity` or `--tenant` to avoid resolving to an unintended identity.

### Environment Variables

Both `COSMOSDB_SHELL_ACCOUNT_KEY` and `COSMOSDB_SHELL_TOKEN` pass credentials through environment variables. Be aware of the following:

- Environment variable values may be visible to **other processes** on the same system (e.g., via `/proc` on Linux or `ps eww` on macOS).
- Values may persist in **shell history** if set inline (e.g., `export COSMOSDB_SHELL_ACCOUNT_KEY=...` in `.bash_history`).
- In CI/CD systems, use **masked secret variables** (Azure Pipelines secrets, GitHub Actions secrets) to prevent credentials from appearing in build logs.
- Avoid setting credential environment variables in shared or multi-user environments.

### MCP Considerations

When running with `--mcp`, the MCP server inherits the shell's connection credentials. It cannot restrict access below what the underlying connection provides.

- If the shell is connected with an **account key**, every MCP client action has full, unrestricted account access — there is no RBAC layer to limit operations.
- If the shell is connected with **Entra ID**, access is governed by the RBAC roles assigned to the authenticated identity, enabling least-privilege scoping.
- The MCP client may relay command outputs and query results to a **remote LLM**. Treat all data returned through MCP as potentially shared with an external service.

**Prefer Entra ID authentication with least-privilege RBAC roles when using MCP**, especially if the MCP client connects to a third-party AI service. See [MCP Security](mcp.md#security) for the full MCP security checklist.
