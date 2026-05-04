# Commands

Parameters with whitespace must be quoted. Escape character: `\`

## Connection

### connect

Connect to a Cosmos DB account. Supports account key, Entra ID, managed identity, and DefaultAzureCredential.

```text
Usage: connect [-hint <ARG>] [-tenant <ARG>] [-authority-host <ARG>] [-mode <ARG>] [-managed-identity <ARG>] connectionString

Arguments:
    connectionString    The account connection string or endpoint URL

Options:
    -hint               Pre-populate username for login prompt
    -tenant             Entra ID tenant ID to authenticate against
    -authority-host     Authority host URL (default: https://login.microsoftonline.com/)
    -mode               Connection mode: 'direct' (default) or 'gateway'
    -managed-identity   Client ID of a user-assigned managed identity
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
    -etag       Optional ETag for optimistic concurrency control
    -database, -db
               Override database name (Optional)
    -container, -con
               Override container name (Optional)
```

`replace` fails when the target item does not already exist.

### patch

Apply a single partial update to an existing item, identified by `id` and partition key.

```text
Usage: patch op id pk path [value] [-etag <ARG>] [-database <ARG>] [-container <ARG>]

Arguments:
    op          Patch operation: set, add, replace, remove, or incr
    id          Item ID
    pk          Partition key value
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

#### Examples

```bash
patch set order-42 customer-7 /status active
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

## Management

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
    partition_key   The partition key path
    [unique_key]    Unique key paths (Optional)
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
Usage: create item [name] [partition_key]

Arguments:
    item            Object type: item, container, or database
    [name]          Container or database name (Optional)
    [partition_key] Partition key for container (Optional)
```

### delete

Delete item, container, or database.

```text
Usage: delete item pattern

Arguments:
    item        Object type: item, container, or database
    pattern     Items/container/database to delete
```

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
