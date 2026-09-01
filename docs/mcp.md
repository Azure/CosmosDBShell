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

Server-side programming commands — stored procedures (`sproc`), user-defined functions (`udf`), and triggers (`trigger`) — are restricted from MCP. Run those commands manually in the shell.

Transactional batches invoked through MCP must use the one-shot `batch run` subcommand. Stateful batch subcommands (`begin`, `add`, `execute`, `cancel`, `status`, and `show`, including their aliases) are restricted to the interactive shell because MCP tool calls share no client-specific batch state.

### Destructive Command Confirmation

Destructive commands (`delete`, `rm`, `rmcon`, `rmdb`) are gated behind an explicit user confirmation. When a client invokes one, the server sends an MCP elicitation prompt describing the exact command line before anything runs:

- **Approved** — the command executes normally.
- **Declined or cancelled** — nothing is executed and the tool call returns an error explaining that the user did not approve.
- **Client cannot confirm** — if the connected client does not support elicitation, the command is refused (fail-closed) and the response suggests running it manually in the shell.

This replaces any opt-in write flag: destructive commands are always allowed to be invoked, but always require confirmation.

The MCP confirmation applies even when a command is invoked with a force / no-prompt argument (for example `rmdb OldDB true`). That argument only skips the *interactive shell* prompt; it does not bypass the MCP elicitation gate.

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
| Accidental destruction | Destructive commands prompt for confirmation before running; review each request and deny anything unexpected |
| Unnecessary exposure | Disable `--mcp` when not needed |

### Checklist

- [ ] Only enable on trusted machines/networks
- [ ] Keep port bound to `127.0.0.1`
- [ ] Use Azure AD/managed identity authentication
- [ ] Review and approve (or deny) destructive operations when prompted
- [ ] Don't share secrets (keys, PII) in prompts or outputs
- [ ] Disable MCP mode when not actively using it

## Tool Results

Every tool result carries the same machine-readable JSON payload in two places:

- **`structuredContent`** — the payload as first-class structured content, for clients that consume MCP structured results.
- **A text content block** — the identical payload serialized as JSON text, so clients that only read text content blocks continue to work.

Both representations are always byte-for-byte equivalent.

### Payload shape

| Field | When present | Description |
| ----- | ------------ | ----------- |
| `result` | Commands that produce output | The command result as JSON (objects, arrays, or a scalar). Text-only results are represented as a JSON string. Failed transactional batches include their per-operation summary here alongside `error`. |
| `outputText` | CSV output commands with non-empty text | The CSV rendering of the result. Omitted when the CSV output is empty or whitespace. |
| `continuationToken` | Paged `query` and container-item `ls` results | Opaque token for the next page, or `null` when no more results are available. Omitted for `query --explain` and database/container name listings, which are not paged. |
| `error` | Failed commands | The error message. |
| `currentLocation` | Always | The shell's current navigation path (for example `/MyDatabase/MyContainer`), or `null` when disconnected. |

Successful results set `result` (and optionally `outputText`); failed results set `error`, may also include a structured `result`, and mark the tool result as an error. `currentLocation` is always included so a client can track navigation state across calls.

### Pagination

MCP calls to `query` and container-item `ls` return one Cosmos DB page per call. When `max` is omitted, the server applies a safe default cap of `100` items. To continue, pass the returned `continuationToken` as the next call's `continuation` argument while keeping the query and other options unchanged. The token is opaque; do not parse or edit it. A `null` token indicates that the result set is exhausted.

`continuation` is exposed only to MCP callers — there is no corresponding shell option, and the token is never echoed into the shell's command output.

Because `max` bounds a single page, a call can return fewer items than requested and still have more available; treat a non-null `continuationToken` as the only signal that more results exist. For `ls`, the `result.limitReached` flag reports that same condition and is kept for parity with shell and script output.

```json
{
	"query": "SELECT * FROM c WHERE c.status = 'active'",
	"max": 50,
	"continuation": "<token from the previous result>"
}
```

