# Programming

This document covers scripts and custom commands in Cosmos Shell.

## Lexical Structure

### Whitespace and Comments

- Spaces/tabs separate tokens; newlines end statements (or use `;`)
- `#` starts a comment to end of line

### Identifiers

- Allowed: letters, digits, `_`, `-`, `.`, `\`, `$`
- Keywords (case-insensitive): `if`, `while`, `for`, `do`, `loop`, `def`, `return`, `break`, `continue`

### Variables

- Form: `$name` (letters/digits/underscore)
- Script args: `$0` = path, `$1`, `$2`... = positional arguments
- Assign: `$name = <expression>`

### Numbers

- Integers: `42`, `314`
- Negatives: `-1`

### Strings

| Type | Syntax | Notes |
| ------ | ------ | ----- |
| Single-quoted | `'text'` | Literal, no escapes. Double `'` for quote: `'it''s'` |
| Double-quoted | `"text"` | Escapes: `\n`, `\r`, `\t`, `\\`, `\"` |
| Interpolated | `$"Hello $name"` | Variable substitution with `$var` |

### JSON Paths

Access piped JSON with dot notation:

```bash
$.items[0].id      # property and array access
```

## Types

| Type | Example |
| ------ | ------- |
| String | `'text'` or `"with $var"` |
| Number | `42`, `3.14` |
| Boolean | `true`, `false` |
| Variable | `$name` |
| JSON | `{ id: "1" }`, `[1,2,3]` |

## Operators

| Category | Operators |
| -------- | --------- |
| Arithmetic | `+` `-` `*` `/` `%` `**` |
| Comparison | `<` `<=` `>` `>=` `==` `!=` |
| Logical | `&&` `\|\|` `^` `!` |
| Grouping | `( ... )` |
| Assignment | `=` `+=` `-=` `*=` `/=` |

## Variable Usage

```bash
$name = "value"           # assign
echo $name                # use
echo $"Hello $name"       # interpolate
```

For script positional parameters, see [Writing and Running Scripts](#writing-and-running-scripts).

## Writing and Running Scripts

Cosmos Shell scripts are plain text files, usually with a `.csh` extension. A script contains the same statements you can type in the interactive shell: commands, assignments, pipes, loops, functions, and `exec`.

Example script:

```bash
# seed.csh
connect $1
cd $2/$3
query "SELECT * FROM c"
```

Run a script by using the script path as the command name and placing script arguments after it:

```bash
seed.csh "AccountEndpoint=...;AccountKey=..." mydb mycontainer
```

Inside the script, positional parameters are available as variables:

| Variable | Value |
| -------- | ----- |
| `$0` | Script path used to start the script |
| `$1` | First script argument |
| `$2` | Second script argument |
| `$3`... | Additional script arguments |

Script arguments are evaluated by the caller before the script starts. Use quotes for values with spaces, semicolons, or shell-significant characters such as connection strings.

### Startup Execution

Use `-c` to run a command or script and exit:

```bash
cosmosdbshell -c "seed.csh \"AccountEndpoint=...;AccountKey=...\" mydb mycontainer"
```

Use `-k` to run a command or script and then stay in the interactive shell:

```bash
cosmosdbshell -k "seed.csh \"AccountEndpoint=...;AccountKey=...\" mydb mycontainer"
```

Startup connection options still belong to the shell process, not to the script. Because everything after `-c` / `-k` is captured as the command, place app-level options before `-c` / `-k`:

```bash
cosmosdbshell --connect "AccountEndpoint=...;AccountKey=..." -c "seed.csh mydb mycontainer"
```

Quotes around the command are optional &mdash; the shell joins all remaining tokens after `-c` / `-k` into a single command string:

```bash
cosmosdbshell --connect "AccountEndpoint=...;AccountKey=..." -c seed.csh mydb mycontainer
```

If you want a value such as `--connect` to be passed to the script, put it inside the `-c` or `-k` command text (after `-c` everything goes to the script anyway):

```bash
cosmosdbshell -c "seed.csh --connect xyz"
```

### Piped Input

When standard input is redirected, the shell reads it as command text. This is useful for running inline scripts:

```bash
echo "connect \"AccountEndpoint=...;AccountKey=...\"; ls" | cosmosdbshell
```

To run a script file with parameters through piped input, pipe a script invocation:

```bash
echo "seed.csh \"AccountEndpoint=...;AccountKey=...\" mydb mycontainer" | cosmosdbshell
```

Piping the contents of a script file directly runs those statements as standard input, so there is no script filename and no positional parameter list for that input stream. Use `-c`, `-k`, or pipe a script invocation when you need `$0`, `$1`, `$2`, and later parameters.

### Script Scope

Each script run gets its own variable scope. Variables from the caller are readable at script start, but assignments inside the script stay local to that script run and do not leak back to the caller.

## Control Flow

### if/else

```bash
if $n > 0 { echo "positive" } else { echo "non-positive" }
```

### while

```bash
$i = 0
while $i < 3 { echo $i; $i = $i + 1 }
```

### for

```bash
for $x in ["a","b","c"] { echo $x }
```

#### Command Expressions

Commands can be used as expressions (for loops, assignments, and parenthesized expressions). This is useful for iterating over command results.

```bash
# Iterate local files
for $file in (dir "*.csh") { echo $file.name }

# Capture a command result
$dbs = (ls)
echo $dbs
```

### exec

The `exec` statement evaluates an expression to get a **command name** or a **script path**, then executes it with optional arguments.

```bash
exec <expression> [arg1] [arg2] ...
```

Notes:

- If the evaluated value is a file path that exists, the shell runs it as a `.csh` script.
- Argument parsing stops at `;`, newline, `}`, or `|` (so you can chain with pipes).

Examples:

```bash
$script = {path: "myscript.csh", name: "My Script"}
exec $script.path arg1 arg2

for $file in (dir "examples/list_dir/*.csh") { exec $file.path }

$cmd = "ls"
exec $cmd -m 5
```

### do-while

```bash
do { echo "tick" } while $condition
```

### loop

```bash
loop {
    if $done { break }
    echo "running"
}
```

### break / continue

```bash
while $true {
    if $skip { continue }
    if $done { break }
}
```

## Custom Commands (def)

Define reusable commands invoked like built-ins.

### Syntax

```bash
def name [param1 param2] { <statements> }
```

### Example

```bash
def greet [who] { echo $"Hello $who" }
greet "Cosmos"
```

### Parameters and Scope

- Arguments available as `$param1`, `$param2` inside body
- Functions have own variable scope (don't leak to caller)
- Globals remain readable

### Returning Values

```bash
def add [a b] { return ($a + $b) }
add 2 3 | echo $"sum=$."
```

- `return` stops execution and sets result
- Returned JSON can be accessed with paths downstream
- Without `return`, function completes with last state

### Functions in Pipelines

```bash
def range3 { return [1,2,3] }
range3 | for $n in $. { echo $"n=$n" }
```

### Practical Example

```bash
def ensure_container [db container pk] {
    mkdb $db
    cd $db
    mkcon $container $pk
    cd $container
}

def seed [count] {
    for $i in [1,2,3,4,5] {
        echo $"[{\"id\":\"item$i\",\"pk\":\"$i\",\"value\":$i}]" | mkitem
    }
}

connect $1
ensure_container sampledb items /pk
seed 5
```

## JSON Path Syntax

```bash
.prop              # property access
.items[0]          # array index
$.items[0].id      # from piped JSON
```

Chain with pipes:

```bash
query "SELECT * FROM c" | $.[0] | .id
```

## Blocks and Pipes

### Blocks

Group statements with `{ ... }`. Separate by newline or `;`.

```bash
{ echo "a"; echo "b" }
```

### Pipes

`|` passes result from left to right command:

```bash
query "SELECT * FROM c" | echo $.items[0]
echo '[{"id":"a"}]' | mkitem
```

## Tips

- Use `def` to encapsulate repeatable sequences
- Return JSON from functions for pipeline consumption
- Use `$"..."` interpolation when composing JSON for `mkitem`
- Prefer `return` when producing values for downstream commands
