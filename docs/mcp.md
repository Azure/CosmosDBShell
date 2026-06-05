# MCP Integration

Model Context Protocol (MCP) allows AI clients to control CosmosDBShell programmatically.

## Start MCP Server

```bash
dotnet run --project CosmosDBShell -- --mcp
dotnet run --project CosmosDBShell -- --mcp 5050
```

Bare `--mcp` starts the HTTP server on the default port `6128`.

## VS Code Setup

> **Requires VS Code 1.103+**

### Enable Autostart (Recommended)

1. Open **Settings** (`Ctrl+,`)
2. Search for `chat.mcp.autostart`
3. Select **newAndOutdated**

MCP servers will start automatically without manual refresh.

### Manual Start

1. Open Command Palette (`Ctrl+Shift+P` / `Cmd+Shift+P`)
2. Run `MCP: List Servers`
3. Select `localCosmosDBShellServer` → **Start Server**
4. Check **Output** tab for startup confirmation

## Resources

The MCP server publishes documentation and live shell-state resources that clients can read or subscribe to:

| URI | Description |
| --- | --- |
| `cosmos://docs/scripting` | Cosmos Shell scripting reference (markdown) |
| `cosmos://docs/nosql-query-language` | Cosmos DB NoSQL query reference (markdown) |
| `cosmos://docs/commands` | JSON catalog of non-restricted shell commands |
| `cosmos://shell/connection` | Current connection (endpoint, scope, ARM context) |
| `cosmos://shell/location` | Current shell location as JSON (`{ "location": "/db/container" }`; `location` is `null` when disconnected) |
| `cosmos://shell/history` | Recent shell commands with `AccountKey` redacted |
| `cosmos://databases` | Databases on the connected account |
| `cosmos://current/containers` | Containers in the current database scope |
| `cosmos://current/container/indexing-policy` | Indexing policy of the current container |

### Subscriptions

The server advertises the `resources.subscribe` capability. Clients may subscribe
to the following URIs and will receive `notifications/resources/updated` on shell
state transitions (`connect`, `disconnect`, `cd`, `rmdb` of the current database):

- `cosmos://shell/connection`
- `cosmos://shell/location`
- `cosmos://databases`
- `cosmos://current/containers`
- `cosmos://current/container/indexing-policy`

All other resources (`cosmos://docs/*`, `cosmos://shell/history`) are read-on-demand
and never push update notifications. Subscriptions to any other URI are rejected,
as are subscriptions beyond a per-client cap.

### Completion

The server advertises the `completions` capability and declares two resource templates that drive argument completion:

| Template | Completable placeholders |
| --- | --- |
| `cosmos://databases/{database}/containers` | `{database}` |
| `cosmos://databases/{database}/containers/{container}/indexing-policy` | `{database}`, `{container}` |

`completion/complete` returns live database names from the connected account, and live container names for the database the client has already supplied in `context.arguments.database`. Empty list when the shell is disconnected.

## Security

### How MCP Works

The MCP server runs locally with your user permissions. Connected clients can execute shell commands, which means they can:

- Read database/container metadata
- Query and retrieve documents
- Create, update, and delete resources

Database and container resource actions are executed through Azure Resource Manager when an ARM context is attached (Entra ID connections). MCP sessions connected with account keys, emulator credentials, or static data-plane tokens fall back to the Cosmos DB data plane for these actions.

For deterministic ARM routing in multi-subscription environments, start the shell with `--connect-subscription` and `--connect-resource-group`.

### Data Exposure

Your MCP client may use a remote LLM. Command outputs, query results, and file contents could be transmitted to external services. **Treat all shell output as potentially shared.**

### Best Practices

| Risk | Mitigation |
| ---- | ---------- |
| DNS rebinding | Origin header validated on all requests; non-loopback origins rejected |
| Unauthorized access | Bind to localhost only, don't expose port publicly |
| Credential leakage | Use Azure AD instead of connection strings/keys |
| Excessive permissions | Apply least-privilege RBAC, narrow scopes |
| Missing management-plane scope | For ARM-routed actions, connect with Entra ID and grant Cosmos DB Operator or equivalent scoped permissions; otherwise the shell falls back to the data plane |
| Accidental destruction | Review tool requests, don't auto-approve deletes |
| Unnecessary exposure | Disable `--mcp` when not needed |

### Checklist

- [ ] Only enable on trusted machines/networks
- [ ] Keep port bound to `127.0.0.1`
- [ ] Use Azure AD/managed identity authentication
- [ ] Review and approve destructive operations manually
- [ ] Don't share secrets (keys, PII) in prompts or outputs
- [ ] Disable MCP mode when not actively using it
