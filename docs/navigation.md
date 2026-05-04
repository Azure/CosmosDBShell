# Navigation and Listing

The shell models Cosmos DB resources as a simple folder-like hierarchy under your connected account.

## Resource Hierarchy

```text
Account → Databases → Containers → Items
```

There are **no folders inside a container** – containers hold items (JSON documents) directly. Use `ls` to list the current level and `cd` to change scope. The `pwd` command prints the current location explicitly.

## Moving Around

### The `ls` Command

The `ls` command lists resources at the current level:

| Context | `ls` shows |
| ------- | ---------- |
| Connected (root) | All databases in the account |
| Inside a database | All containers in that database |
| Inside a container | Items (documents) in that container |

**Options:**

| Option | Description |
| ------ | ----------- |
| `-m <n>` | Limit results to first n items (container scope). Default is 100 when omitted; use 0 or a negative value for no limit |
| `-f <fmt>` | Output format (for example: `table`) |
| `--db <name>` | Override database name |
| `--con <name>` | Override container name |
| `--key <prop>` | When listing items, match the filter against this item property (defaults to the container partition key property) |

**Examples:**

```bash
ls                  # list current level; in a container this defaults to first 100 items
ls -m 10            # list first 10 items
ls -m 0             # list all matching items without a limit
ls "*active*" --key status
ls --db MyDb        # list containers in a specific database
ls --db MyDb --con Items -m 5
```

If `ls` reaches the effective limit while listing container items, it prints a runtime message telling you the results were limited.

Tip: to iterate local files in scripts, use `dir`:

```bash
for $script in (dir "examples/list_dir/*.csh") { echo $script.name }
```

### The `cd` Command

The `cd` command changes your current scope:

| From | Command | Result |
| ---- | ------- | ------ |
| Connected | `cd <database>` | Enter that database |
| Database | `cd <container>` | Enter that container |
| Any level | `cd ..` | Go up one level |
| Any level | `cd` | Return to connected (root) state |

**Path chaining:**

You can chain path segments to navigate multiple levels at once:

```bash
cd MyDatabase/MyContainer    # enter database and container in one step
cd ../OtherContainer         # switch to sibling container
cd ../../OtherDb/OtherCont   # switch database and container
```

The Cosmos DB hierarchy has at most two levels (`/database/container`). Paths
that resolve below that depth are rejected with an error. From inside a
container, plain names like `cd customers` do not navigate to a sibling
container. Use `cd ../customers` or a fully qualified absolute path such as
`cd /MyDatabase/customers`.

**Navigation patterns:**

```bash
# Start from connected state
cd ToDoList                  # enter database
cd Items                     # enter container
ls -m 5                      # list first 5 items

# Quick switch between containers
cd ../Users                  # switch to Users container in same database
cd                           # return to root (connected state)
```

### The `pwd` Command

The `pwd` command prints the current shell location:

```bash
pwd                  # not connected
connect "AccountEndpoint=...;AccountKey=..."
pwd                  # /
cd ToDoList
pwd                  # /ToDoList
cd Items
pwd                  # /ToDoList/Items
```

## Pipes and JSON Flow

The `|` operator pipes the JSON result of the left command into the right command. This enables powerful command chaining.

### How It Works

1. **Most commands return JSON** – even `ls` returns a structured result like `{ "items": [...] }`
2. **The next command receives that JSON** as its input
3. **Use JSON paths** (starting with `$`) to extract values from piped JSON

### JSON Path Syntax in Pipes

When a command receives piped JSON, you can use path expressions to access specific values:

| Path | Description |
| ---- | ----------- |
| `$` | The entire piped JSON object |
| `$.property` | Access a property |
| `$.items[0]` | Access array element |
| `.[0]` | Shorthand for first element (when result is array-like) |

### Practical Examples

**Navigate to first database returned by ls:**

```bash
ls | cd .[0]
```

**Show the ID of the first item in current container:**

```bash
ls -m 1 | echo $.items[0].id
```

**Create multiple items from a JSON array:**

```bash
echo '[{"id":"a","name":"Alice"},{"id":"b","name":"Bob"}]' | mkitem
```

**Extract nested properties from arbitrary JSON:**

```bash
echo '{"user":{"name":"Ada","email":"ada@example.com"}}' | echo $.user.name
# Output: Ada
```

**Chain multiple operations:**

```bash
# Get the first database, enter it, list containers
ls | cd .[0]; ls
```

**Query and process results:**

```bash
ls -q "SELECT c.id, c.status FROM c WHERE c.priority = 1" | echo $.items
```

### Pipe-Aware Commands

These commands accept and process piped JSON:

| Command | Pipe behavior |
| ------- | ------------- |
| `mkitem` | Creates item(s) from piped JSON |
| `echo` | Outputs piped value or extracts path |
| `cd` | Can use path to select target |
| `delete` | Deletes item specified by piped JSON |
| `jq` | Filters/transforms piped JSON |
| `ftab` | Formats piped JSON as table |

## CLI Arguments

Start the shell with options to customize behavior:

| Option | Description |
| ------ | ----------- |
| `-c <cmd>` | Execute command and exit. Everything after `-c` is taken as the command, so app-level options must come before `-c`. Windows-style `/c` is also accepted. |
| `-k <cmd>` | Execute command and stay in shell. Everything after `-k` is taken as the command. Windows-style `/k` is also accepted. |
| `--connect <str>` | Connect with this connection string or endpoint on startup |
| `--connect-mode <mode>` | Connection mode at startup: 'direct' or 'gateway' |
| `--connect-tenant <id>` | Entra ID tenant ID at startup |
| `--connect-hint <hint>` | Login hint for browser auth at startup |
| `--connect-authority-host <url>` | Authority host URL at startup |
| `--connect-managed-identity <id>` | User-assigned managed identity client ID at startup |
| `--mcp [port]` | Enable MCP (Model Context Protocol) server on the given port, or `6128` by default |
| `--cs <n>` | Color scheme: 0=off, 1=standard, 2=truecolor |
| `--clearhistory` | Clear command history on start |
| `--help` | Show usage information |
| `--version` | Show version |

### Environment Variables

| Variable | Description |
| -------- | ----------- |
| `COSMOSDB_SHELL_TOKEN` | Pre-obtained Entra ID access token (JWT) for single-shot auth |
| `COSMOSDB_SHELL_ACCOUNT_KEY` | Account key for authentication |
| `COSMOSDB_SHELL_FORMAT` | Default output format |
| `COSMOSDB_SHELL_CSVSEP` | CSV column separator |

**Examples:**

```bash
# Run a query and exit
cosmosdbshell -c "connect $CONN; cd mydb/mycont; ls -m 5"

# Start connected to a specific account
cosmosdbshell --connect "AccountEndpoint=...;AccountKey=..."

# Start with MCP server enabled on the default port (6128)
cosmosdbshell --mcp

# Start with MCP server enabled on a custom port
cosmosdbshell --mcp 5050
```
