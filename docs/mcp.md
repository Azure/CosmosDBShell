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

## Security

### How MCP Works

The MCP server runs locally with your user permissions. Connected clients can execute shell commands, which means they can:

- Read database/container metadata
- Query and retrieve documents
- Create, update, and delete resources

### Data Exposure

Your MCP client may use a remote LLM. Command outputs, query results, and file contents could be transmitted to external services. **Treat all shell output as potentially shared.**

### Best Practices

| Risk | Mitigation |
| ---- | ---------- |
| DNS rebinding | Origin header validated on all requests; non-loopback origins rejected |
| Unauthorized access | Bind to localhost only, don't expose port publicly |
| Credential leakage | Use Azure AD instead of connection strings/keys |
| Excessive permissions | Apply least-privilege RBAC, narrow scopes |
| Accidental destruction | Review tool requests, don't auto-approve deletes |
| Unnecessary exposure | Disable `--mcp` when not needed |

### Checklist

- [ ] Only enable on trusted machines/networks
- [ ] Keep port bound to `127.0.0.1`
- [ ] Use Azure AD/managed identity authentication
- [ ] Review and approve destructive operations manually
- [ ] Don't share secrets (keys, PII) in prompts or outputs
- [ ] Disable MCP mode when not actively using it
