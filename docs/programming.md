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
|------|--------|-------|
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
|------|---------|
| String | `'text'` or `"with $var"` |
| Number | `42`, `3.14` |
| Boolean | `true`, `false` |
| Variable | `$name` |
| JSON | `{ id: "1" }`, `[1,2,3]` |

## Operators

| Category | Operators |
|----------|-----------|
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

Script args: `$0` = path, `$1`, `$2`... = arguments

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
