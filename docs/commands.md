# Commands

Parameters with whitespace must be quoted. Escape character: `\`

## Connection

### connect

Connect to a Cosmos DB account. Supports account key, Entra ID, managed identity, and DefaultAzureCredential.

```text
Usage: connect [-hint <ARG>] [-tenant <ARG>] [-authority-host <ARG>] [-mode <ARG>] [-managed-identity <ARG>] [-subscription <ARG>] [-resource-group <ARG>] connectionString

Arguments:
    connectionString    The account connection string or endpoint URL

Options:
    -hint               Pre-populate username for login prompt
    -tenant             Entra ID tenant ID to authenticate against
    -authority-host     Authority host URL (default: https://login.microsoftonline.com/)
    -mode               Connection mode: 'direct' (default) or 'gateway'
    -managed-identity   Client ID of a user-assigned managed identity
    -subscription       Azure subscription ID for ARM database and container operations
    -resource-group     Azure resource group name for ARM database and container operations
```

See [docs/connect.md](connect.md) for detailed credential flow documentation.

### disconnect

Disconnect the current connection.

```text
Usage: disconnect
```

## Diagnostics

### whoami

Show the authenticated identity and credential type for the current connection.

For Microsoft Entra ID connections (interactive browser, device code, managed
identity, Visual Studio Code, static token, or `DefaultAzureCredential`) it
acquires a Cosmos DB access token and decodes the principal id, tenant id,
application id, user principal name, display name, and identity type from the
token claims. The token expiry is taken from the acquired token's metadata
(`ExpiresOn`) rather than the `exp` claim. The token signature is not validated;
the claims are used for local display only.

Account-key and emulator connections use a master key and have no Entra
identity, so only the credential type is reported.

```text
Usage: whoami [--format=<table|json|csv>]

Options:
    --format, -f    Output format: table (default), json, or csv
```

Data-plane RBAC role assignments are a control-plane concept and are not
reported. The default interactive report is rendered as a table; use
`--format json` or `--format csv` for machine-readable output. Redirected output
is written in the selected format (JSON by default, or `table`/`csv` when
`COSMOSDB_SHELL_FORMAT` or `--format` selects them). The
`COSMOSDB_SHELL_FORMAT` environment variable sets the default format. This
command is read-only.

### can-i

Probe whether the current identity can perform an action against a container
without mutating data.

```text
Usage: can-i <action> [--database=<name>] [--container=<name>] [--format=<table|json|csv>]

Arguments:
    <action>            The action to probe: read, query, write, or manage

Options:
    --database, -db     Target database name (defaults to the current database)
    --container, -con   Target container name (defaults to the current container)
    --format, -f        Output format: table (default), json, or csv
```

The probe issues a safe, non-mutating data-plane request and reports `allow`,
`deny`, or `indeterminate` based on the response:

- `read` — a point read of a random id.
- `query` — a minimal `SELECT TOP 1` query with a page size of 1.
- `write` — a delete of a random, almost-certainly-nonexistent id (non-mutating).
  A `deny` means no delete permission; an `allow` is a heuristic inference that
  write access is present.
- `manage` — cannot be probed on the data plane without a mutating or
  control-plane operation, so it is always reported as `indeterminate`.

Account-key and emulator connections use a master key and are reported as
`allow` with method `key`. Entra connections use method `probe` and include the
HTTP status code observed. The default interactive report is rendered as a
table; use `--format json` or `--format csv` for machine-readable output.
Redirected output is written in the selected format (JSON by default, or
`table`/`csv` when `COSMOSDB_SHELL_FORMAT` or `--format` selects them). The
`COSMOSDB_SHELL_FORMAT` environment variable sets the default format. This
command does not mutate data.

## Navigation

### ls

List databases, containers, or items.

```text
Usage: ls [-m <ARG>] [-f <ARG>] [filter]

Arguments:
    [filter]    Filter pattern (Optional)

Options:
    -max, -m    Maximum number of items returned when listing container items. Defaults to 100; use 0 or a negative value for no limit
    -format, -f Output format
    -database, -db
               Override database name (Optional)
    -container, -con
               Override container name (Optional)
    -key, -k    Match filter against this property when listing items in a container (Optional)
```

When `ls` is listing items from a container, it defaults to `100` items if `--max` is not specified. If the limit is hit at runtime, the shell prints a message telling you the results were limited. Use `--max <n>` to choose another limit or `--max 0` or a negative value to disable the limit.

`ls` always prints a summary line for how many results it found, and the line names the scope it listed: when listing databases it reports the database count (or `no databases found.`), when listing containers it reports the container count for the database (or `no containers found in database ...`), and when listing items it reports the item count for the container (or `no items found in container ...`). The summary makes it clear when a scope is genuinely empty versus when the listing simply returned nothing.

When listing databases or containers over an Azure Resource Manager (ARM) connection returns nothing at all, `ls` also prints a warning hint pointing at the most common non-empty causes (the connected identity may lack control-plane read access, or you may be connected to the wrong account). This avoids a silent empty result being mistaken for an empty account or database. Data-plane connections do not show this hint, since an empty result there is genuinely empty.

### cd

Change scope to database or container.

```text
Usage: cd [item]

Arguments:
    [item]      Database or container to select (Optional)

Examples:
    cd MyDb/MyContainer   # chain paths
    cd ..                 # go up
    cd                    # return to root
```

The Cosmos DB hierarchy has at most two levels (`/database/container`), so
paths that would resolve below `/database/container` are rejected. From
inside a container, plain names like `cd customers` do not navigate to a
sibling container; use `cd ../customers` or a fully qualified absolute path
such as `cd /MyDb/customers`. See [Navigation](navigation.md) for more.

### pwd

Show the current shell location.

```text
Usage: pwd
```

Examples:

```bash
pwd                    # not connected
connect "AccountEndpoint=...;AccountKey=..."
pwd                    # /
cd MyDb
pwd                    # /MyDb
cd MyContainer
pwd                    # /MyDb/MyContainer
```

## Appearance

### theme

Inspect, switch, load, validate, save, edit, open, and reload shell color themes.

```text
Usage: theme [action] [name] [path] [-force] [-strict]

Arguments:
    [action]    What to do: current (default), list, show, use (alias: set),
                load, validate, save, edit, open, or reload
    [name]      Theme name (for show/use/save/edit) or a TOML path
                (for load/validate/edit)
    [path]      Optional path for save, load, or validate

Options:
    -force, -f  Overwrite an existing file when saving, or seed the
                built-in profile to a user file when editing
    -strict     Treat warnings as errors during validate
```

Examples:

```bash
theme list
theme show light
theme use light
theme load ./my-theme.toml
theme validate ./my-theme.toml
theme validate ~/.cosmosdbshell/themes
theme validate my-theme --strict
theme save my-theme --force
theme edit my-theme
theme open
theme reload
```

`theme edit` opens the named theme's TOML file in an external editor and reloads it when the editor exits. Built-in profiles have no editable file by default; pass `--force` to seed a copy under `~/.cosmosdbshell/themes` and edit that. `theme open` opens the user themes folder in your OS file browser.

`theme validate` parses a TOML file and reports warnings without registering it or switching the active theme. When the argument is a directory it validates every `*.toml` file in that directory and prints a per-file summary. With no argument it scans the user themes directory (`~/.cosmosdbshell/themes`). The validator collects every issue in a single pass so that multiple typos can be fixed at once, and suggests the closest valid token when an unknown color or modifier is used. It also warns on bracket cycles that have only one color or contain duplicates. Pass `--strict` to fail when any warnings are present. Color values must be empty or one ANSI 16 color name. Style values may combine modifiers with at most one ANSI 16 color.

## Data Operations

### query

Execute SQL query.

```text
Usage: query [-m <ARG>] [-mx <ARG>] [-f <ARG>] [--bucket <ARG>]
             [--database <ARG>] [--container <ARG>] [--explain] query

Arguments:
    query       The query to execute

Options:
    --max, -m             Maximum number of items returned. Use 0 or a negative value
                          for no limit
    --metrics, -mx        Show query metrics (Display or File)
    --format, -f          Output format (json, table, csv)
    --bucket              The throughput bucket to use for the query
    --database, -db       The database to query against
    --container, -con     The container to query against
    --explain             Show the query execution plan (index usage and a
                          plain-language evaluation) instead of returning documents
```

`query` does not apply a default item limit. Use `--max <n>` to cap returned items when needed, or `--max 0` to disable the limit explicitly.

When called through MCP, `query` and container-item `ls` return one page at a time and default to at most `100` items when `max` is omitted. The MCP result includes `continuationToken`; pass a non-null value back as the tool's `continuation` argument, with the same query and options, to retrieve the next page. A null token means there are no more pages. `continuation` is an MCP-only argument and is deliberately not a shell option, so interactive and scripted commands are unaffected and retain their existing multi-page behavior.

#### Explain a query

`query "<sql>" --explain` reports how the query engine resolved the query rather than returning documents. When Cosmos DB returns index metrics, it shows whether the query performed a full scan or an index seek, lists the utilized and potential indexes, the index hit ratio, and the request charge. If index metrics are unavailable or unrecognized, the scan type is reported as unknown instead of assuming a full scan. A plain-language summary highlights confirmed full scans and recommends indexes to add.

```text
query "SELECT * FROM c WHERE c.city = 'Seattle'" --explain
```

To keep the cost low, `--explain` executes only the first page of the query (`MaxItemCount = 1`), so the reported metrics are an estimate based on that page. `--max` is ignored when `--explain` is supplied.


### print

Get item by id and partition key.

```text
Usage: print id key

Arguments:
    id          The ID of the item
    key         The partition key of the item
```

### mkitem

Create items in container (reads JSON from pipe).

```text
Usage: mkitem [-force] [data]

Arguments:
    [data]      JSON data for the item to create or upsert (Optional)

Options:
    -force, -upsert
               Create or replace items (upsert behavior)
    -database, -db
               Override database name (Optional)
    -container, -con
               Override container name (Optional)
```

When `--force` is specified, `mkitem` performs upsert behavior (create if missing, replace if existing).

### replace

Replace existing items in container (reads JSON from argument or pipe).

```text
Usage: replace [data] [-etag <ARG>] [-database <ARG>] [-container <ARG>]

Arguments:
    [data]      JSON object or array of objects to replace (Optional)

Options:
    -etag       Optional ETag for optimistic concurrency control (single item only)
    -database, -db
               Override database name (Optional)
    -container, -con
               Override container name (Optional)
```

`replace` derives the partition key from the item JSON and supports hierarchical partition key containers. The `--etag` option is only supported for single-object input because each item has its own ETag. `replace` fails when the target item does not already exist.

### patch

Apply a single partial update to an existing item, identified by `id` and partition key.

```text
Usage: patch op id pk path [value] [-etag <ARG>] [-database <ARG>] [-container <ARG>]

Arguments:
    op          Patch operation: set, add, replace, remove, or incr
    id          Item ID
    pk          Partition key value. Use a JSON literal for typed keys, or a JSON array for hierarchical partition keys.
    path        JSON path to the target field (must start with '/')
    [value]     Value for the operation (omit for 'remove')

Options:
    -etag       Optional ETag for optimistic concurrency control
    -database, -db
               Override database name (Optional)
    -container, -con
               Override container name (Optional)
```

#### Operations

|Op|Requires value|Behavior|
|-|-|-|
|`set`|Yes|Sets the field at `path`. Creates the field if it does not exist. Safest default for changing a value.|
|`add`|Yes|Cosmos JSON-Patch `add`. For object properties this looks like `set`. For array indices it inserts at the index, shifting existing elements. Use `/-` to append to the end of an array.|
|`replace`|Yes|Replaces the value at `path`. Fails if the field does not already exist.|
|`remove`|No|Deletes the field at `path`. Must not be given a `value`.|
|`incr`|Yes (numeric)|Adds the numeric value (positive or negative) to the existing numeric value at `path`. The target must be a number.|

The alias `increment` is also accepted for `incr`.

#### Value typing

The `value` argument is parsed as JSON when it looks like a JSON literal. Otherwise it is sent as a plain string. This makes typed values feel natural in the shell:

|You write|Sent as|
|-|-|
|`active`|`"active"` (string)|
|`42`|`42` (number)|
|`3.14`|`3.14` (number)|
|`true` / `false`|boolean|
|`null`|JSON null|
|`"hello world"`|`"hello world"` (string with spaces)|
|`'{"x":1}'`|object `{ "x": 1 }`|
|`'[1,2,3]'`|array `[1, 2, 3]`|

Quoting rules follow the shell: wrap values that contain spaces or shell metacharacters in quotes. Use single quotes around JSON object/array literals so the shell does not try to interpret them.

The `pk` argument follows the same JSON-literal typing for numbers, booleans, and null. For hierarchical partition keys, pass the key components as a JSON array, for example `'["tenant-1","order-42"]'`.

#### Examples

```bash
patch set order-42 customer-7 /status active
patch set order-42 '["tenant-1","customer-7"]' /status active
patch set order-42 customer-7 /count 42
patch set order-42 customer-7 /name "Ada Lovelace"
patch incr order-42 customer-7 /viewCount 1
patch incr order-42 customer-7 /stock -2
patch remove order-42 customer-7 /oldField
patch add order-42 customer-7 /tags/0 urgent      # inserts "urgent" at index 0, shifting existing tags right
patch add order-42 customer-7 /tags/- archived    # appends "archived" to the end of the tags array
patch replace order-42 customer-7 /profile/email "ada@example.com"
patch set order-42 customer-7 /name "Ada Lovelace" --etag="<etag-from-read>"
```

#### Errors

- Missing item: `Item '<id>' not found.`
- ETag mismatch when `--etag` is supplied: `Item '<id>' was modified since it was last read (ETag mismatch).`
- `replace` against a missing field, or `incr` against a non-numeric field, surfaces a Cosmos `BadRequest` with the underlying reason.
- `remove` with a `value` argument is rejected up front.
- `incr` with a non-numeric value is rejected up front.

### batch

Execute multiple write operations against a single partition key as one atomic Cosmos DB transactional batch. Either run a batch in a single call, or build one up statefully across several commands. Every operation in a batch must share the same partition key, execution requires between 1 and 100 operations, and if any operation fails the entire batch is rolled back. A pending stateful batch may be empty until operations are added.

```text
Usage: batch subcommand [data] [--partition-key <ARG>] [-database <ARG>] [-container <ARG>]

Arguments:
    subcommand  The action to perform: run, begin, add, execute, cancel, status, or show
    [data]      Batch operations as a JSON array, or a single operation as a JSON object (Optional)

Options:
    --partition-key, --pk
               The partition key shared by every operation in the batch. Required for `run` and `begin`; the stateful `add`, `execute`, `cancel`, `status`, and `show` subcommands use the active batch's partition key.
    -database, -db
               Override database name (Optional)
    -container, -con
               Override container name (Optional)
```

#### Subcommands

|Subcommand|Description|
|-|-|
|`run <json> --partition-key <pk>`|Parse a JSON array of operations and execute them atomically in a single call. Also reads piped input.|
|`begin --partition-key <pk>`|Start a stateful batch bound to a partition key, database, and container.|
|`add <json>`|Queue one operation (JSON object) or several (JSON array) onto the active batch.|
|`execute` (`exec`, `commit`)|Commit the queued operations atomically and clear the active batch.|
|`cancel` (`abort`)|Discard the active batch without executing it.|
|`status`|Report the active batch target, partition key, and a compact list of queued operations.|
|`show`|Print the queued operations as a JSON array (the same shape accepted by `run`/`add`).|

When a stateful batch is active the prompt shows a `[batch:N]` indicator, where `N` is the number of queued operations.

In interactive user output, `status` uses a compact table while `show` always prints the full queued-operation JSON. Use `--output json`, redirection, or a pipeline to obtain the structured JSON result from `status`.

#### Operation schema

Each operation is a JSON object with an `op` field:

|Operation|Shape|
|-|-|
|`create`|`{"op":"create","item":{...}}`|
|`upsert`|`{"op":"upsert","item":{...}}`|
|`replace`|`{"op":"replace","id":"1","item":{...}}` (the `id` is optional when `item.id` is present)|
|`delete`|`{"op":"delete","id":"3"}`|
|`patch`|`{"op":"patch","id":"1","operations":[{"op":"set","path":"/name","value":"x"}]}`|

Patch sub-operations use the same `op`/`path`/`value` shape and semantics as the [`patch`](#patch) command (`set`, `add`, `replace`, `remove`, `incr`). Patch values are typed JSON values, for example `"value":"active"`, `"value":42`, or `"value":true`.

#### Result

In interactive user output, `batch run` and `batch execute` print a concise outcome with the operation count and request charge. Their full result is emitted as a JSON summary when using `--output json`, redirection, a pipeline, or MCP:

```json
{
  "success": true,
  "statusCode": 200,
  "requestCharge": 12.34,
  "operationCount": 2,
  "operations": [
    { "index": 0, "op": "create", "statusCode": 201, "id": "a", "etag": "..." },
    { "index": 1, "op": "delete", "statusCode": 204, "id": "b" }
  ]
}
```

When the batch fails, `success` is `false`, the failing operation reports its own status code, and the remaining operations report `424` (Failed Dependency) because the transaction was rolled back.

#### Examples

```bash
batch run '[{"op":"create","item":{"id":"1","pk":"a"}},{"op":"delete","id":"2"}]' --partition-key a
echo '[{"op":"upsert","item":{"id":"3","pk":"a"}}]' | batch run --partition-key a

batch begin --partition-key a
batch add '{"op":"upsert","item":{"id":"3","pk":"a"}}'
batch add '{"op":"patch","id":"3","operations":[{"op":"set","path":"/status","value":"done"}]}'
batch status
batch show       # prints the queued operations as a JSON array
batch execute
```

#### Errors

- Missing `--partition-key` for `run` or `begin` is rejected up front.
- `add`, `execute`, or `cancel` with no active batch: `No batch is in progress. Start one with 'batch begin'.`
- `begin` while a batch is already active: `A batch is already in progress. Run 'batch execute' or 'batch cancel' first.`
- More than 100 operations is rejected before any call to Cosmos DB.
- A transactional failure prints a one-line message, returns a result summary with `success` set to `false` and per-operation status codes, and rolls back every operation.

### rm

Remove items from container.

```text
Usage: rm pattern [options]

Arguments:
    pattern     Pattern for items to remove

Options:
    --database, --db
                Database containing the items to remove
    --container, --con
                Container containing the items to remove
    --key, -k   Property name to match the pattern against (defaults to partition key)
    --dry-run   Preview how many items would be deleted without deleting them
```

Examples:

- `rm test-*` deletes every item whose partition key starts with `test-`.
- `rm test-* --dry-run` reports how many items match without deleting anything.

### export

Stream items from a container to a local file. Default format is JSON Lines (one compact JSON object per line); pass `--format=array` for a single JSON array, or `--format=csv` for CSV. Items are streamed end-to-end for JSON formats; CSV buffers items to compute the column set. The CSV separator follows the `COSMOSDB_SHELL_CSVSEP` environment variable (default `;`).

```text
Usage: export <file> [options]

Arguments:
    file                 Destination file path.

Options:
    --db, --database     Source database (defaults to the current navigation context).
    --con, --container   Source container (defaults to the current navigation context).
    --query, -q          SELECT query whose results are exported (default: SELECT * FROM c).
    --max, -m            Maximum number of items to export. 0 means no limit.
    --format, -f         Output format: jsonl (default), array, or csv.
    --force              Overwrite the destination file if it already exists.
```

Examples:

- `export items.jsonl` exports every item in the current container.
- `export active.jsonl --query="SELECT * FROM c WHERE c.status = 'active'"` exports a filtered subset.
- `export snapshot.json --format=array --force` exports as a JSON array, replacing any existing file.
- `export items.csv --format=csv` exports as CSV with one column per top-level property (nested values are written as compact JSON).

The summary line reports the number of items written and the total RU charge.

### import

Bulk-load items from a JSON Lines, JSON array, or CSV file into a container. Format is auto-detected: a `.csv` extension selects CSV, otherwise the first non-whitespace character is inspected (`[` ⇒ array, otherwise JSON Lines). It can be forced with `--format`. Default mode is `insert`; pass `--mode=upsert` to replace items that already exist. For CSV, the header row defines property names and every value is imported as a string; the CSV separator follows `COSMOSDB_SHELL_CSVSEP` (default `;`). JSON Lines and JSON array inputs are streamed item-by-item, but CSV import reads and parses the entire file into memory before importing, so very large CSV files can cause a significant memory spike.

```text
Usage: import <file> [options]

Arguments:
    file                          Source file path.

Options:
    --db, --database              Target database (defaults to the current navigation context).
    --con, --container            Target container (defaults to the current navigation context).
    --mode                        Write mode: insert (default) or upsert.
    --format, -f                  Input format: auto (default), jsonl, array, or csv.
    --partition-key, --pk         For CSV import, the partition key path. Nested paths
                                  (e.g. /address/city) nest the matching column.
    --continue, --continue-on-error
                                  Continue importing after individual item failures.
    --dry-run                     Parse the file without writing any items (validation only).
```

Examples:

- `import items.jsonl` inserts every item from a JSON Lines file.
- `import items.json --format=array` reads a JSON array file.
- `import items.csv` imports a CSV file, mapping each header column to a string property.
- `import items.csv --partition-key=/address/city` nests the `city` column under `address` for a nested partition key. If a scalar column already occupies an intermediate path segment (for example an `address` column), the import fails with a conflict error rather than silently overwriting it.
- `import items.jsonl --mode=upsert --continue-on-error` upserts items and keeps going on per-item failures.
- `import items.jsonl --dry-run` validates the file without writing anything; useful before a real run.

By default, the first failure stops the import. With `--continue-on-error` the command keeps going after per-item *write* failures (for example a Cosmos write that throws) and the final summary reports how many items succeeded and how many failed. Parse and validation errors (invalid JSON, non-object rows, CSV partition-key conflicts) still abort the import immediately. The command exits with an error status if any items failed.

### watch

Tail the change feed of a container, printing new and modified items as they arrive. Also available as `tail`.

```text
Usage: watch [-from-beginning] [-partition-key <ARG>] [-max <ARG>] [-interval <ARG>] [-format <ARG>] [-database <ARG>] [-container <ARG>]

Options:
    -from-beginning, -b
               Replay the change feed from the beginning of the container instead of from now
    -partition-key, -pk
               Scope the change feed to a single partition key (Optional)
    -max, -m   Stop after this many changes (Optional)
    -interval, -i
               Seconds between change feed polls; defaults to 1 (Optional)
    -format, -f
               Output format for the printed items (Optional)
    -database, -db
               Override database name (Optional)
    -container, -con
               Override container name (Optional)
```

By default `watch` starts from now and follows the container, printing each change as highlighted JSON until you press Ctrl+C. Use `--from-beginning` to replay existing items first, `--partition-key` to scope the feed to one partition, and `--max` to stop automatically after a number of changes. Use `--interval` to change how long the shell waits between polls once it has caught up (default 1 second; values below 0.1 are clamped to avoid hammering the container). The change feed surfaces creates and updates (not deletes). This command is interactive and streaming, so it is not exposed over MCP.

```bash
watch
watch --from-beginning
watch --partition-key=myKey --max=100
watch --interval=5
watch --database=MyDB --container=Products
```

## Scripting

### exec

Execute a command or script determined at runtime (statement).

```text
Usage: exec <expression> [argument ...]

Arguments:
    expression    Evaluates to a command name or a script path
    [argument]    Optional arguments passed to the executed command/script
```

Notes:

- If `expression` evaluates to an existing file path, it is executed as a `.csh` script.

Examples:

```bash
$script = {path: "myscript.csh"}
exec $script.path arg1 arg2

for $file in (dir "*.csh") { exec $file.path }
```

### edit

Open a local file (for example a `.csh` script) in an external editor and wait for the editor to close. The file is created if it does not already exist. Pair it with `exec` to edit and then run a script.

```text
Usage: edit <path>

Arguments:
    path        The file to edit (created if it does not exist)
```

Examples:

```bash
edit deploy.csh   # open in $EDITOR (or platform default)
exec deploy.csh   # run the script you just edited
```

The editor is resolved from `$VISUAL`, then `$EDITOR`, then a platform default (`notepad` on Windows, `nano` elsewhere). GUI editors must block until the file is closed (for example by setting `$VISUAL` to `code --wait`), otherwise the command returns immediately. `edit` requires an interactive terminal and is rejected when input is piped or running under a script.

## Management

Database and container management commands prefer Azure Resource Manager when an ARM context is attached (Entra ID connections, optionally specifying `--subscription` and `--resource-group` for explicit account targeting). The account name is inferred from the endpoint. Account-key, emulator, and static-token connections do not attach ARM context, so these commands automatically fall back to the Cosmos DB data plane and use the connection's existing credentials.

### mkdb

Create database.

```text
Usage: mkdb name

Arguments:
    name        The database name to create
```

### mkcon

Create container.

```text
Usage: mkcon name partition_key [unique_key]

Arguments:
    name            The container to create
    partition_key   The partition key path. For hierarchical partition keys, use comma-separated paths such as /tenantId,/userId,/sessionId
    [unique_key]    Unique key paths (Optional)

Examples:
    mkcon Products /categoryId
    mkcon Orders /customerId,/orderId
```

### rmdb

Remove database.

```text
Usage: rmdb name [force] [options]

Arguments:
    name        The database to remove
    [force]     Skip the confirmation prompt when `true` (Optional)

Options:
    --dry-run   Preview the deletion without deleting the database
```

### rmcon

Remove container.

```text
Usage: rmcon name [force] [options]

Arguments:
    name        The container to remove
    [force]     Skip the confirmation prompt when `true` (Optional)

Options:
    --database, --db
                Database containing the container to remove
    --dry-run   Preview the deletion without deleting the container
```

### create

Create item, container, or database.

```text
Usage: create item [name] [partition_key] [-force]

Arguments:
    item            Object type: item, container, or database
    [name]          JSON data for item, or container/database name (Optional)
    [partition_key] Partition key for container (Optional)

Options:
    -force, -upsert
                    Create or replace items when creating an item (upsert behavior)
```

### delete

Delete item, container, or database.

```text
Usage: delete item pattern [options]

Arguments:
    item        Object type: item, container, or database
    pattern     Items/container/database to delete

Options:
    --database, --db
                Database to target for item/container deletes (forwarded to rm/rmcon)
    --container, --con
                Container to target for item deletes (forwarded to rm)
    --dry-run   Preview the deletion without applying it
```

The `--dry-run` flag is forwarded to the underlying `rm`, `rmcon`, or `rmdb` operation, so `delete item test-* --dry-run` previews the affected items without deleting them.

### index

Manage the indexing policy of a container through subcommands. Aliased as `indexpolicy`.

```text
Usage: index subcommand [paths ...] [-mode <ARG>] [-automatic <ARG>] [-database <ARG>] [-container <ARG>]

Arguments:
    subcommand  show, add, remove, or set
    [paths]     One or more index paths (for add/remove), or a full indexing policy JSON document (for set)

Options:
    -mode, -m   Indexing mode for 'set' (consistent or none)
    -automatic, -a
                Automatic indexing flag for 'set' (true or false)
    -database, -db
                Override database name (Optional)
    -container, -con
                Override container name (Optional)
```

#### Subcommands

|Subcommand|Behavior|
|-|-|
|`show`|Reads and returns the current indexing policy as JSON.|
|`add <path...>`|Adds one or more paths to the included paths. Existing paths are left untouched, and any matching excluded path is removed.|
|`remove <path...>`|Removes one or more paths from both the included and excluded paths.|
|`set`|Updates the indexing policy. Pass `--mode` (`consistent` or `none`, case-insensitive) and/or `--automatic` to patch the current policy, or provide a full indexing policy JSON document to replace it.|

Paths use the Cosmos DB indexing path syntax, for example `/address/*` or `/name/?`.

#### Examples

```bash
index show
index add /address/*
index add /address/* /name/?
index remove /address/*
index set --mode=consistent --automatic=true
index set '{"indexingMode":"consistent","automatic":true,"includedPaths":[{"path":"/*"}],"excludedPaths":[]}'
```

### schema

Infer the schema of a container from a small, bounded sample. The command returns the
partition key path(s), an indexing policy summary, an estimated document count, and the
field types inferred from the sample. It is read-only and uses a bounded sampling query
along with a container metadata read, making it a cheap way for agents and users to
discover a container's structure without re-sampling or guessing field names.

```text
Usage: schema [-sample <ARG>] [-fields-only] [-database <ARG>] [-container <ARG>]

Options:
    -sample, -s Maximum number of documents to sample (1-100, default 20)
    -fields-only, -short
                Return only sampledDocuments and inferred fields without reading container metadata
    -database, -db
                Override database name (Optional)
    -container, -con
                Override container name (Optional)
```

By default the command targets the current container. Use `--database` and `--container`
to target a specific resource. The `--sample` value is clamped to the range 1-100 so the
discovery query stays bounded both server-side and in the client.

Inferred fields use dot notation for nested objects (for example `address.city`). Each
field lists the distinct JSON types observed (`string`, `number`, `boolean`, `object`,
`array`, or `null`) and the number of sampled documents in which the field was present.
The `indexingPolicy` summary contains `indexingMode`, `automatic`, `includedPaths`,
`excludedPaths`, `compositeIndexes`, `spatialIndexes`, and `vectorIndexes`.

#### Examples

```bash
schema
schema --sample=50
schema --fields-only
schema --short
schema --database=MyDB --container=Products
```

`--fields-only` (alias `--short`) skips the container metadata read and returns only
`sampledDocuments` and `fields`. This is useful when only field names and observed JSON
types are needed and a smaller CLI or MCP result is preferred.

Short output:

```json
{
    "sampledDocuments": 20,
    "fields": [
        { "path": "id", "types": ["string"], "presence": 20 },
        { "path": "price", "types": ["null", "number"], "presence": 18 }
    ]
}
```

Abbreviated sample output (the `indexingPolicy` summary is omitted for brevity):

```json
{
  "database": "MyDB",
  "container": "Products",
  "partitionKeyPaths": ["/category"],
  "documentCountEstimate": 1280,
  "sampleSize": 20,
  "sampledDocuments": 20,
  "fields": [
    { "path": "id", "types": ["string"], "presence": 20 },
    { "path": "category", "types": ["string"], "presence": 20 },
    { "path": "price", "types": ["number"], "presence": 18 }
  ]
}
```

### throughput

View or change the provisioned throughput (RU/s) of a database or container through subcommands.

```text
Usage: throughput subcommand [ru] [-database <ARG>] [-container <ARG>]

Arguments:
    subcommand  show, set, manual, or autoscale
    [ru]        Throughput in RU/s (manual RU/s for set/manual, maximum RU/s for autoscale)

Options:
    -database, -db
                Override database name (Optional)
    -container, -con
                Override container name (Optional)
    -yes, -y, -force
                Skip the confirmation prompt before applying a change (Optional)
    -dry-run    Preview the change without applying it (Optional)
```

By default the command targets the current scope: the container when in a container, otherwise the database. Use `--database` and `--container` to target a specific resource.

#### Subcommands

|Subcommand|Behavior|
|-|-|
|`show`|Reads and returns the current throughput as JSON, including the mode (`manual`, `autoscale`, or `none`), provisioned RU/s, autoscale maximum, and minimum.|
|`set <RUs>`|Sets manual throughput to the given RU/s. Alias of `manual`.|
|`manual <RUs>`|Switches to manual provisioning at the given RU/s.|
|`autoscale <maxRUs>`|Switches to autoscale with the given maximum RU/s.|

Throughput changes apply to the resource's own provisioned throughput. Containers inside a shared-throughput database, and serverless accounts, have no dedicated throughput to change.

Throughput values are validated before the request is sent: manual RU/s must be at least 400 and a multiple of 100, and autoscale maximum RU/s must be at least 1000 and a multiple of 1000.

Switching between `manual` and `autoscale` is a mode migration. Over an Azure AD (token) connection this is performed automatically. Over a key-based (data-plane) connection the SDK cannot migrate modes, so a mode switch is rejected with guidance to use a token connection, the Azure portal, Azure CLI, or PowerShell; changing the RU/s value within the current mode still works.

Write operations (`set`, `manual`, `autoscale`) ask for confirmation before applying, because throughput changes can affect your bill. Pass `--yes` (`-y`/`--force`) to skip the prompt. The prompt is also skipped automatically in non-interactive contexts (MCP, script execution, or piped input).

Pass `--dry-run` with a write subcommand to preview the change without applying it. The command reads the current throughput and reports the current vs. planned mode and RU/s as JSON (and a table interactively); no write is performed and no confirmation is required.

#### Examples

```bash
throughput show
throughput set 4000
throughput manual 4000
throughput autoscale 10000
throughput set 4000 --yes
throughput autoscale 10000 --dry-run
throughput show --database MyDatabase --container MyContainer
```

### ttl

View or change the time-to-live (TTL) of a container through subcommands.

```text
Usage: ttl subcommand [seconds] [-analytical] [-database <ARG>] [-container <ARG>]

Arguments:
    subcommand  show, set, on, or off
    [seconds]   Time-to-live in seconds for the set subcommand (must be positive)

Options:
    -analytical, -a
                Target the analytical store TTL instead of the default item TTL (Optional)
    -database, -db
                Override database name (Optional)
    -container, -con
                Override container name (Optional)
```

The command operates on a container. By default it targets the current container. Use `--database` and `--container` to target a specific container.

#### Subcommands

|Subcommand|Behavior|
|-|-|
|`show`|Reads and returns the current TTL configuration as JSON. `status` is `disabled` (items never expire), `no-default` (TTL is on but items expire only when they carry their own `ttl` property), or `enabled` (items expire after `defaultTimeToLiveSeconds`).|
|`set <seconds>`|Enables TTL with a positive default expiration in seconds.|
|`on`|Enables TTL with no container default (equivalent to a default TTL of `-1`); only items with their own `ttl` property expire.|
|`off`|Disables TTL so items never expire.|

The seconds value is validated before the request is sent: `set` requires a positive number, and `show`, `on`, and `off` reject a seconds argument.

#### Analytical store TTL

Pass `--analytical` (or `-a`) to operate on the container's analytical store TTL instead of the default item TTL. The analytical store must be supported by the account.

|Subcommand|Behavior with `--analytical`|
|-|-|
|`show`|Returns the analytical status (`disabled` or `enabled`) and `analyticalTimeToLiveSeconds`.|
|`set <seconds>`|Retains analytical data for a positive number of seconds.|
|`on`|Enables the analytical store with indefinite retention (a TTL of `-1`).|
|`off`|Disables the analytical store.|

#### Examples

```bash
ttl show
ttl set 86400
ttl on
ttl off
ttl show --database MyDatabase --container MyContainer
ttl show --analytical
ttl set 2592000 --analytical
ttl on --analytical
ttl off --analytical
```

### conflict

View or change the conflict resolution policy of a container through subcommands.

```text
Usage: conflict subcommand [-mode <ARG>] [-path <ARG>] [-procedure <ARG>] [-database <ARG>] [-container <ARG>]

Arguments:
    subcommand  show or set

Options:
    -mode, -m   Conflict resolution mode: lastWriterWins or custom (Optional)
    -path, -p   Resolution path for lastWriterWins mode, for example /_ts (Optional)
    -procedure, -proc, -sproc
                Stored procedure id that resolves conflicts for custom mode (Optional)
    -database, -db
                Override database name (Optional)
    -container, -con
                Override container name (Optional)
```

The command operates on a container. By default it targets the current container. Use `--database` and `--container` to target a specific container.

#### Subcommands

|Subcommand|Behavior|
|-|-|
|`show`|Reads and returns the current policy as JSON, including the mode (`LastWriterWins` or `Custom`), the resolution path (last-writer-wins), and the resolution stored procedure (custom).|
|`set`|Updates the policy. Pass `--mode` to choose `lastWriterWins` or `custom`. For last-writer-wins pass `--path` to name the property that decides the winner (defaults to `/_ts`). For custom pass `--procedure` to name the stored procedure that resolves conflicts. Options that are not supplied keep their current value.|

`--path` applies only to last-writer-wins mode and `--procedure` applies only to custom mode; combining them with the wrong mode is rejected before the request is sent.

Conflict resolution policies only take effect on accounts configured for multi-region writes.

#### Examples

```bash
conflict show
conflict set --mode lastWriterWins --path /_ts
conflict set --mode custom --procedure resolveConflicts
conflict show --database MyDatabase --container MyContainer
```

### sproc

Manage JavaScript stored procedures on a container through subcommands.

```text
Usage: sproc subcommand [name] [value] [-partition-key <ARG>] [-force] [-database <ARG>] [-container <ARG>]

Arguments:
    subcommand  list, show, exists, create, exec, edit, or delete
    [name]      The stored procedure id
    [value]     A JavaScript file (for create) or a JSON array of arguments (for exec)

Options:
    -partition-key, -pk
                Partition key used to target a partition when executing (required for exec)
    -force, -f  Replace the stored procedure if it already exists (create)
    -database, -db
                Override database name (Optional)
    -container, -con
                Override container name (Optional)
```

#### Subcommands

|Subcommand|Behavior|
|-|-|
|`list`|Lists the stored procedures in the current container. The interactive table shows id, last modified, and body size; the structured JSON result contains `id`, `lastModified`, `etag`, and `bodyLength` for each.|
|`show <name>`|Returns the body of a stored procedure.|
|`exists <name>`|Returns a boolean indicating whether a stored procedure exists. The boolean result can be used directly in `if` and `while` conditions.|
|`create <name> <file>`|Creates a stored procedure from a JavaScript file. The body can also be piped in. Pass `--force` to replace an existing one.|
|`create <name>`|With no file or piped body, seeds a sample stored procedure, opens it in an external editor, and prompts to create or discard on exit. Interactive sessions only; scripts must pass a file. The `sproc` command is not available over MCP.|
|`exec <name> [params]`|Executes a stored procedure. `params` is a JSON array of arguments, and `--partition-key` selects the target partition.|
|`edit <name>`|Opens an existing stored procedure body in an external editor and saves it on exit. Fails if the stored procedure does not exist; use `create` to add a new one. Interactive sessions only; not available over MCP or from scripts.|
|`delete <name>`|Deletes a stored procedure.|

#### Examples

```bash
sproc list
sproc show myProc
sproc exists myProc
sproc create myProc ./myProc.js
sproc create myProc ./myProc.js --force
sproc create myProc
sproc edit myProc
sproc exec myProc '["param1", "param2"]' --partition-key pk1
sproc delete myProc
```

Stored procedures are a Cosmos DB for NoSQL feature. The `sproc` command operates on the current container, the same scope as `index`.

### udf

Manage JavaScript user-defined functions (UDFs) on a container through subcommands.

```text
Usage: udf subcommand [name] [value] [-force] [-database <ARG>] [-container <ARG>]

Arguments:
    subcommand  list, show, exists, create, edit, or delete
    [name]      The user-defined function id
    [value]     A JavaScript file (for create)

Options:
    -force, -f  Replace the user-defined function if it already exists (create)
    -database, -db
                Override database name (Optional)
    -container, -con
                Override container name (Optional)
```

#### Subcommands

|Subcommand|Behavior|
|-|-|
|`list`|Lists the user-defined functions in the current container. The interactive table shows id and body size; the structured JSON result contains `id`, `etag`, and `bodyLength` for each.|
|`show <name>`|Returns the body of a user-defined function.|
|`exists <name>`|Returns a boolean indicating whether a user-defined function exists. The boolean result can be used directly in `if` and `while` conditions.|
|`create <name> <file>`|Creates a user-defined function from a JavaScript file. The body can also be piped in. Pass `--force` to replace an existing one.|
|`create <name>`|With no file or piped body, seeds a sample user-defined function, opens it in an external editor, and prompts to create or discard on exit. Interactive sessions only; scripts must pass a file. The `udf` command is not available over MCP.|
|`edit <name>`|Opens an existing user-defined function body in an external editor and saves it on exit. Fails if the user-defined function does not exist; use `create` to add a new one. Interactive sessions only; not available over MCP or from scripts.|
|`delete <name>`|Deletes a user-defined function.|

#### Examples

```bash
udf list
udf show myFunc
udf exists myFunc
udf create myFunc ./myFunc.js
udf create myFunc ./myFunc.js --force
udf create myFunc
udf edit myFunc
udf delete myFunc
```

User-defined functions are a Cosmos DB for NoSQL feature invoked from within queries. The `udf` command operates on the current container, the same scope as `index`. Like `sproc` and `trigger`, it is restricted from MCP and must be run manually in the shell.

### trigger

Manage JavaScript triggers on a container through subcommands.

```text
Usage: trigger subcommand [name] [value] [-type <ARG>] [-operation <ARG>] [-force] [-database <ARG>] [-container <ARG>]

Arguments:
    subcommand  list, show, exists, create, edit, or delete
    [name]      The trigger id
    [value]     A JavaScript file (for create)

Options:
    -type, -t   Trigger type for create: pre or post (required for create)
    -operation, -op
                Operation the trigger fires on: all, create, replace, delete, or update (default: all)
    -force, -f  Replace the trigger if it already exists (create)
    -database, -db
                Override database name (Optional)
    -container, -con
                Override container name (Optional)
```

#### Subcommands

|Subcommand|Behavior|
|-|-|
|`list`|Lists the triggers in the current container. The interactive table shows id, type, operation, and body size; the structured JSON result contains `id`, `triggerType`, `triggerOperation`, `etag`, and `bodyLength` for each.|
|`show <name>`|Returns the body of a trigger.|
|`exists <name>`|Returns a boolean indicating whether a trigger exists. The boolean result can be used directly in `if` and `while` conditions.|
|`create <name> <file>`|Creates a trigger from a JavaScript file. The body can also be piped in. `--type` selects `pre` or `post`, `--operation` selects the operation (defaults to `all`), and `--force` replaces an existing one.|
|`create <name> --type <pre\|post>`|With no file or piped body, seeds a sample trigger, opens it in an external editor, and prompts to create or discard on exit. `--type` is still required. Interactive sessions only; scripts must pass a file. The `trigger` command is not available over MCP.|
|`edit <name>`|Opens an existing trigger body in an external editor and saves it on exit, preserving the trigger type and operation. Fails if the trigger does not exist; use `create` to add a new one. Interactive sessions only; not available over MCP or from scripts.|
|`delete <name>`|Deletes a trigger.|

#### Examples

```bash
trigger list
trigger show myTrigger
trigger exists myTrigger
trigger create myTrigger ./myTrigger.js --type pre --operation create
trigger create myTrigger ./myTrigger.js --type post --operation all --force
trigger create myTrigger --type pre
trigger edit myTrigger
trigger delete myTrigger
```

Triggers are a Cosmos DB for NoSQL feature. Pre-triggers and post-triggers are invoked when item operations opt in to them. The `trigger` command operates on the current container, the same scope as `index`. Like `sproc` and `udf`, it is restricted from MCP and must be run manually in the shell.

## Utilities

### az

Execute Azure CLI command.

```text
Usage: az [args]

Arguments:
    [args]      Arguments to pass to az (Optional)
```

### echo

Print message; useful to pipe text/JSON.

```text
Usage: echo message

Arguments:
    message     The message to print
```

### cat

Display file contents.

```text
Usage: cat [path]

Arguments:
    [path]      File path to view (Optional)
```

### dir

List files and directories in the local file system.

```text
Usage: dir [-d <ARG>] [-r] [-l] [filter]

Arguments:
    [filter]        File name pattern filter (Optional, default: *)

Options:
    -directory, -d  The directory to list files from (Optional, default: current directory)
    -recursive, -r  List files recursively in subdirectories
    -list, -l       Show file names only (simple list format)
```

Notes:

- If you pass a directory path as the positional argument and omit `--directory`, it is treated as the directory to list (filter becomes `*`).
- The JSON result is an array of entries with: `name`, `path`, `isDirectory`, `size`, `lastModified`.

### jq

Command-line JSON processor.

```text
Usage: jq [args]

Arguments:
    [args]      Arguments to pass to jq (Optional)
```

### filter

Native JSON filter and transformation command. Uses the built-in filter
expression language, a small jq-inspired subset designed for shell-safe JSON
shaping. The full grammar and semantics are documented in
[filter-v1-spec.md](./filter-v1-spec.md).

```text
Usage: filter expression

Arguments:
    expression  Filter expression to evaluate against piped JSON input
```

Notes:

- `filter` requires piped JSON input.
- Results stay structured JSON in the shell pipeline, so `filter` composes
  cleanly with later commands (for example `filter ... | ftab`).
- `filter` is not a full jq implementation. If you need jq features that v1
  does not implement (regex, `reduce`, `def`, `|=`, multi-result `,`, etc.),
  use the external `jq` command when it is installed.

#### Quick reference

| Construct | Meaning |
|---|---|
| `.` | The current input |
| `.name` / `."Volcano Name"` / `.["Volcano Name"]` | Property access |
| `.name?` | Optional property access — returns `null` instead of erroring on a wrong type |
| `.[0]` | Array index access |
| `.[]` | Array iteration (materialized to a JSON array at the top level) |
| `.foo[0]?`, `.[]?` | Optional index / iteration |
| `a | b` | Pipe — evaluate `b` against the result of `a` |
| `==` `!=` `<` `<=` `>` `>=` | Comparison operators producing booleans |
| `+` `-` `*` `/` `%` `**` | Arithmetic on numbers (`**` is power, right-associative); unary `-`/`+` also work |
| `&&` `\|\|` `^` `!` | Logical and / or / xor / not (`&&` and `\|\|` short-circuit) |
| `[expr, ...]` | Array constructor; each expression sees the current input |
| `{id, status}` | Object shorthand — equivalent to `{id: .id, status: .status}` |
| `{id: .id, "item-id": .id}` | Explicit object construction with identifier or string keys |
| `length` | Length of array, object, string, or `null` (number and boolean raise a runtime error) |
| `keys` | Sorted array of an object's property names |
| `type` | One of `"null"`, `"boolean"`, `"number"`, `"string"`, `"array"`, `"object"` |
| `contains(expr)` | Substring / element / object-subset / equality test |
| `map(expr)` | Apply `expr` to each element of an input array |
| `select(expr)` | Keep array elements where `expr` evaluates to `true` |
| `sort_by(expr)` | Sort an input array by the key produced by `expr` (cross-type keys order by `null` < `false` < `true` < number < string < array < object) |

#### Examples

Project a single field from a query result:

```text
query "SELECT * FROM c" | filter '.values[0]'
```

Count items returned by a command:

```text
ls | filter '.values | length'
```

Shape each item into a smaller object:

```text
query "SELECT * FROM c" | filter '.values | map({id, status})'
```

Project items with quoted property names:

```text
ls | filter '.values | map({"Volcano Name": .["Volcano Name"], Country})'
```

Filter items by a predicate:

```text
query "SELECT * FROM c" | filter '.values | select(.status == "active")'
```

Sort and project:

```text
query "SELECT * FROM c" | filter '.values | sort_by(.id) | map(.id)'
```

Collect iterated values into a flat array:

```text
query "SELECT * FROM c" | filter '[.values[] | .id]'
```

Combine with `ftab` to render the projected JSON as a table:

```text
query "SELECT * FROM c" | filter '.values | map({id, status})' | ftab
```

#### Quoting

The expression is parsed by `filter`, not by the shell, but the shell still
tokenizes the argument first. Wrap the expression in single quotes so the
shell does not interpret characters such as `|`, `$`, or `"` inside it:

```text
filter '.values | select(.status == "active")'
```

If you need a literal single quote inside the expression, prefer double quotes
on the outside or escape per your platform's shell rules.

### ftab

JSON to table processor.

```text
Usage: ftab [-f <ARG>] [-take <ARG>] [-sort <ARG>] [-colorize <ARG>] [-format <ARG>]

Options:
    -fields, -f Comma-separated field names to include in the table (Optional)
    -take       Limit the number of rendered rows (Optional)
    -sort       Sort rows by a field before rendering. Use field or field:asc|desc (Optional)
    -colorize   Colorize terminal cells using field:value:style rules separated by ';' (Optional)
    -format     Output format: default, markdown, or html (Optional)
```

### bucket

Manage throughput buckets: client-side bucket selection plus container bucket limits.

```text
Usage: bucket [action] [id] [percent] [-database <db>] [-container <con>] [-yes]

Arguments:
    [action]    A bucket id (0-5) to select client-side, or show, set, or clear for
                container limits (Optional)
    [id]        The throughput bucket id (1-5) to set or clear a limit for (Optional)
    [percent]   The maximum percentage (1-100) of container throughput the bucket
                may use (Optional)

Options:
    -database, -db    The database to target, or that contains the target container (Optional)
    -container, -con  The container whose throughput bucket limits to read or change (Optional)
    -yes, -y, -force  Skip the confirmation prompt before changing a bucket limit (Optional)
```

The `bucket` command has two surfaces:

- **Client-side selection** (works on any connected database or container scope):
  - `bucket` shows this client's current throughput bucket selection.
  - `bucket <1-5>` tags this client's requests with the given throughput bucket.
  - `bucket 0` clears the client-side selection.
- **Container limits** (read with `show`, change with `set`/`clear`):
  - `bucket show` lists the throughput bucket limits configured on the current container.
  - `bucket set <1-5> <1-100>` limits a bucket to a maximum percentage of the container's throughput.
  - `bucket clear <1-5>` removes a bucket's configured limit.

The container-limit subcommands operate on the current container (or the one named by
`-container`) and require provisioned throughput. They are control-plane operations that
are only available on an Azure AD (Entra) connection; on key-based or emulator connections
they return an error directing you to the portal, Azure CLI, or PowerShell. The client-side
`bucket <0-5>` selection still works on any connection.

### info

Show configuration and usage statistics for the current container, database, or
account, depending on what is in scope.

When a container is in scope it reports the partition key, throughput (min/max
RU/s), analytical TTL, geospatial and full-text policies, a compact indexing
policy summary (indexing mode, automatic flag, and included/excluded path counts
plus any composite, spatial, or vector index counts), plus the document count and
data/total storage size. Use `index show` for the full indexing policy JSON. When
only a database is in scope it reports the container count, aggregate document
count, total storage, and shared throughput. When neither is in scope (the
account root) it reports account metadata: read/write regions and the database
count.

On serverless accounts, throughput/offer settings are not available, so the
scale section reports that throughput settings are not available for serverless
accounts instead of failing.

```text
Usage: info [--partitions] [--detailed] [--format=<user|json|table|csv>] [--database=<name>] [--container=<name>]

Options:
    --partitions, -p    Add the per-physical-partition document distribution (consumes request units)
    --detailed, -d      Add storage breakdown and top partition keys (performs a full scan and consumes request units)
    --format, -f        Output format: user, json, table, or csv
    --database, -db     Target database name
    --container, -con   Target container name
```

The default interactive report is rendered as tables. Use `--format json` for a
machine-readable JSON object; redirecting `info` output to a file also writes JSON
so CI and scripts can parse the result directly. When `--format table` is combined
with redirection, the report is written to the file as a plain-text grid instead of
the rich console layout. The `--partitions` and
`--detailed` options issue queries against the data and therefore consume
request units; at the account root, `--detailed` aggregates every container's
storage and document count across all databases. This command is read-only.

### help

Show help for commands.

```text
Usage: help [-details] [-plain] [command]

Arguments:
    [command]       Command to show help for (Optional)

Options:
    -details, -d    Show detailed help for all commands
    -plain          Disable colors/styling
```

### version

Display version.

```text
Usage: version
```

### welcome

Display the welcome screen.

```text
Usage: welcome
```

The welcome screen is shown automatically on the first interactive startup. Later
startups show a compact line containing the shell version and MCP server status.

### cls

Clear the console screen.

```text
Usage: cls
```

Alias: `clear`

**Keyboard shortcut**: `Ctrl+L`

### exit

Exit Cosmos DB shell.

```text
Usage: exit
```
