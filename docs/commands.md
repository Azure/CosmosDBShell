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
Usage: query [-m <ARG>] query

Arguments:
    query       The query to execute

Options:
    -max, -m    Maximum number of items returned. Use 0 or a negative value for no limit
```

`query` does not apply a default item limit. Use `--max <n>` to cap returned items when needed, or `--max 0` to disable the limit explicitly.

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

### rm

Remove items from container.

```text
Usage: rm pattern

Arguments:
    pattern     Pattern for items to remove
```

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
Usage: rmdb name

Arguments:
    name        The database to remove
```

### rmcon

Remove container.

```text
Usage: rmcon name

Arguments:
    name        The container to remove
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
Usage: delete item pattern

Arguments:
    item        Object type: item, container, or database
    pattern     Items/container/database to delete
```

### index

Manage the indexing policy of a container through subcommands.

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

#### Examples

```bash
throughput show
throughput set 4000
throughput manual 4000
throughput autoscale 10000
throughput set 4000 --yes
throughput show --database MyDatabase --container MyContainer
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
query "SELECT * FROM c" | filter '.items[0]'
```

Count items returned by a command:

```text
ls | filter '.items | length'
```

Shape each item into a smaller object:

```text
query "SELECT * FROM c" | filter '.items | map({id, status})'
```

Project items with quoted property names:

```text
ls | filter '.items | map({"Volcano Name": .["Volcano Name"], Country})'
```

Filter items by a predicate:

```text
query "SELECT * FROM c" | filter '.items | select(.status == "active")'
```

Sort and project:

```text
query "SELECT * FROM c" | filter '.items | sort_by(.id) | map(.id)'
```

Collect iterated values into a flat array:

```text
query "SELECT * FROM c" | filter '[.items[] | .id]'
```

Combine with `ftab` to render the projected JSON as a table:

```text
query "SELECT * FROM c" | filter '.items | map({id, status})' | ftab
```

#### Quoting

The expression is parsed by `filter`, not by the shell, but the shell still
tokenizes the argument first. Wrap the expression in single quotes so the
shell does not interpret characters such as `|`, `$`, or `"` inside it:

```text
filter '.items | select(.status == "active")'
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

Get or set SDK throughput bucket.

```text
Usage: bucket [bucket]

Arguments:
    [bucket]    Bucket number: 0=clear, 1-5=valid buckets (Optional)
```

### settings

Show account overview or container settings.

```text
Usage: settings
```

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
