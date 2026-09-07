# Programming

This document covers scripts and custom commands in Cosmos Shell.

## Lexical Structure

### Whitespace and Comments

- Spaces/tabs separate tokens; newlines end statements (or use `;`)
- `#` starts a comment to end of line

### Identifiers

- Allowed: letters, digits, `_`, `-`, `.`, `\`, `$`
- Keywords (case-insensitive): `if`, `else`, `while`, `for`, `in`, `do`, `loop`, `def`, `return`, `break`, `continue`, `exec`

### Variables

- Form: `$name` (letters/digits/underscore)
- Script args: `$0` = path, `$1`, `$2`... = positional arguments
- Assign: `$name = <expression>`

Variable names are case-sensitive. For compatibility, the lexer also accepts hyphens in variable names: `$name-1` refers to a variable named `name-1`, not subtraction. Use spaces around arithmetic and assignment operators, for example `$name - 1` and `$name -= 1`.

### Numbers

- Integers: `42`, `314`
- Negatives: `-1`
- Decimal literals: `3.14`, `3.0`

Integer literals use signed 32-bit values. Arithmetic between integers stays integer arithmetic, including truncating division (`3 / 2` is `1`). Integer overflow raises an error instead of wrapping. Use a decimal operand for floating-point arithmetic (`3.0 / 2` is `1.5`). Decimal values use IEEE 754 `double`, not exact base-10 decimal arithmetic.

JSON numbers use the same rules in expressions and `for` loops: integer-form values within the `Int32` range become integers; fractional, exponent-form, or larger values use `double`. Large JSON integers can therefore lose precision beyond the exact range of `double`. For example, a JSON property containing `3` divided by `2` produces `1`, while a property containing `3.0` produces `1.5`.

JSON construction is a separate conversion boundary: a shell decimal with an integral value can be serialized without its fractional suffix. For example, `$object = {"value":3.0}` currently stores JSON `3`, so `$object.value / 2` uses integer division. Use a decimal divisor (`2.0`) when floating-point division is required after JSON construction.

Numeric Boolean conversion uses zero versus nonzero, including for fractional and large JSON numbers. JSON numbers use the same `double` conversion as decimal shell values for this check, so `if 1.5` and `if $object.value` behave alike when the property contains `1.5`.

JSON `null` remains JSON `null` when bound by a `for` loop or passed through a function. Rebuilding an array from that value produces `[null]`, not `["null"]`. Text conversion remains explicit and separate from JSON type preservation.

### Strings

The `+` operator concatenates when either operand is a shell string or a JSON string, including values read through JSON paths or passed as function arguments. Numeric-looking strings remain text: two JSON properties containing `"2"` concatenate to `"22"`, not `4`.

| Type | Syntax | Notes |
| ------ | ------ | ----- |
| Single-quoted | `'text'` | Literal, no escapes. Double `'` for quote: `'it''s'` |
| Double-quoted | `"text $name"` | Literal `$`; escapes: `\n`, `\r`, `\t`, `\\`, `\"`, `\uXXXX` |
| Interpolated | `$"Hello $name"` | Variable and expression substitution with `$var` and `$(...)` |

Interpolation is enabled only by the explicit `$"..."` prefix. Use `$(...)` for expressions, for example `$"Total: $($foo + $bar)"`. The complete contents must form an expression, and command expressions are not allowed inside an interpolated string. Within an interpolated string, escape a literal dollar sign as `\$`. In ordinary double-quoted strings, `$name` and `$(...)` remain literal text and are never evaluated.

### JSON Paths

Access piped JSON with dot notation:

```bash
$.values[0].id      # property and array access
```

## Types

| Type | Example |
| ------ | ------- |
| String | `'text'` or `"literal $var"` |
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

Precedence, from lowest to highest:

| Level | Operators | Associativity |
| --- | --- | --- |
| 1 | `\|\|` | Left |
| 2 | `&&` | Left |
| 3 | `^` | Left |
| 4 | `==`, `!=` | Left |
| 5 | `<`, `<=`, `>`, `>=` | Left |
| 6 | `+`, `-` | Left |
| 7 | `*`, `/`, `%` | Left |
| 8 | `**` | Right |
| 9 | Unary `!`, `+`, `-` | Right |

Parentheses override precedence. Unary operators bind more tightly than power: `-2 ** 2` is `4`; use `-(2 ** 2)` for `-4`. `&&` and `||` short-circuit; other binary operators evaluate each operand exactly once. Assignment is a statement, not an expression. Compound assignments use the same arithmetic rules as their corresponding binary operators and evaluate the right-hand side once.

### Statement Grammar

The following EBNF summarizes statement structure; command arguments retain shell-word quoting and option syntax. Expressions follow the precedence table above and include literals, variables, JSON construction, paths, and parenthesized command calls.

```ebnf
script     = { statement, [ separator ] } ;
separator  = ";" | newline ;
statement  = simple, { "|", simple } ;
simple     = assignment | command | block
           | "if", expression, statement, [ "else", statement ]
           | "while", expression, statement
           | "do", statement, "while", expression
           | "for", variable, "in", expression, statement
           | "loop", statement
           | "def", name, [ parameters ], statement
           | "return", [ expression ] | "break" | "continue"
           | "exec", expression, { argument } ;
block      = "{", script, "}" ;
assignment = variable, ( "=" | "+=" | "-=" | "*=" | "/=" ), expression ;
parameters = "[", { name }, "]" | "(", [ name, { ",", name } ], ")" ;
```

### Validation and Errors

Each command text or script file is fully parsed and checked for invalid control-flow placement and duplicate function parameters before any of its statements execute. Syntax or semantic errors prevent execution of that entire input. Script files are checked when invoked, including calls through `exec` and command expressions; callers are not recursively preflighted against dynamically selected files.

Runtime failures stop execution but do not roll back earlier successful commands. A failed command expression propagates an error rather than silently producing an empty result. Cancellation is checked between block statements and loop iterations, including loops without database commands.

Host-requested cancellation propagates through script files, blocks, loops, and function calls without being converted into a positional runtime error. The shell reports a neutral result and records cancellation in the diagnostic log; call scopes and source context are restored. Cancellation exceptions without a canceled host token still follow the existing error/timeout handling.

Parser errors from script files retain their own filename and source text, including when reached through a command expression. Runtime exceptions retain their original cause and exit-code category: attaching a source location does not turn authentication, throttling, connectivity, or arithmetic failures into usage errors.

Calling a function with too few or too many arguments is a usage error (exit code `2`), including calls within expressions. The function body is not executed.

Functions defined in a script retain the definition's source location even when invoked later from another file. Runtime diagnostics show the innermost source location first, followed by the recorded function/script call sites in human-readable output. JSON error messages include the originating file, line, and column. Diagnostic logs retain source locations and underlying exception details through the existing secret-redaction pipeline.

The language server applies the same control-flow and duplicate-parameter validation as script execution. File-level `return` is valid; `break` and `continue` require an enclosing loop in the same function. Diagnostics use exclusive-end editor ranges and are refreshed when a document changes.

The language server also recognizes case-sensitive function names declared in the current document, including recursive calls and calls from other function bodies. It checks commands and built-in options inside blocks, branches, loops, pipelines, and command expressions. Function-name discovery is document-wide: it does not prove that a definition has executed before a call, or resolve functions loaded dynamically from other files. Runtime registration and execution order are unchanged.

Variable symbols and hover lookups are also case-sensitive: `$value` and `$Value` remain distinct. Variable analysis still treats the first occurrence of each name as its definition and does not model the runtime's call scopes.

### Resource Limits

The parser has a shared nesting budget of 128 recursive parsing entries. Statements, expression operators, primary expressions, and interpolation subparsers share this budget, so the allowed number of source-level parentheses depends on the surrounding syntax. Exceeding it produces a parser diagnostic before execution, including in editor/highlighter parsing. Sequential statements do not accumulate nesting depth.

Expression trees also have a maximum depth of 128 nodes, including operators and containing expressions such as parentheses, arrays, objects, and interpolation. This independent check rejects long flat operator chains that do not require deep parser recursion. Overdeep expressions are replaced with error nodes before execution or editor analysis; the containing input is not executed. Split a long expression into intermediate assignments when needed.

At most 64 function and script-file calls may be active at once, including mixed or indirect recursion. Exceeding this limit produces a runtime error, unwinds call scopes, and leaves the interpreter usable. Calls also check cancellation before entering a new scope. These are fixed safety limits, not a sandbox or a wall-clock timeout; long-running valid scripts still require host cancellation.

## Variable Usage

```bash
$name = "value"           # assign
echo $name                # use
echo $"Hello $name"       # interpolate
echo "Hello $name"        # print $name literally
```

### Session request charge variables

The shell provides three built-in session variables:

| Variable | Description |
| --- | --- |
| `$sessionRequestCharge` | Read-only cumulative request charge observed during the current connection. |
| `$sessionChargedOperationCount` | Read-only number of command operations that reported a positive charge. |
| `$sessionRequestChargeWarningThreshold` | Configurable warning threshold in RUs. A positive value enables the warning; `0` disables it. |

For example, `$sessionRequestChargeWarningThreshold = 100` prints one warning when the
current connection reaches or exceeds 100 observed RUs. The warning is emitted
only once for that threshold. A successful `connect` resets the accumulated
charge and operation count and rearms the warning, while preserving the
configured maximum. Assigning a different positive maximum also rearms the
warning for the new threshold.

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

This also applies when a script is called as an expression. `return [expression]` exits the current script file and supplies its result, including from nested blocks or loops. A return inside a function exits only that function. Script positional arguments remain text values, unlike typed function parameters.

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
while true {
    if $skip { continue }
    if $done { break }
}
```

`break` exits the nearest enclosing loop; `continue` skips the rest of its current iteration. Nested blocks and conditionals preserve these signals. Neither may cross a function or script-file boundary. Bare blocks do not introduce variable scopes.

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

- Arguments are available as `$param1`, `$param2` inside the body and retain their numeric, boolean, text, or JSON types. Unquoted shell words are text.
- The argument count must exactly match the parameter count. Duplicate parameter names are rejected.
- Functions have their own variable scope. Assignments, including compound assignments to existing outer variables, stay local.
- Variables are read from the nearest active call frame, then outer caller frames and globals. Functions do not capture lexical closures. Frames are removed on success, failure, and cancellation.
- Return a value to update a caller variable explicitly, for example `$total = (add $total 1)`. Session settings such as `$sessionRequestChargeWarningThreshold` remain session-wide.

### Returning Values

```bash
def add [a b] { return ($a + $b) }
add 2 3 | echo $"sum=$."
```

- `return` stops the current function even inside nested blocks or loops and sets its result. A bare `return` has no result and can appear immediately before `}`, as in `def empty { return }`; no semicolon is required there. Outside a function or script file, `return` is rejected.
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
.values[0]          # array index
$.values[0].id      # from piped JSON
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
query "SELECT * FROM c" | echo $.values[0]
echo '[{"id":"a"}]' | mkitem
```

## Tips

- Use `def` to encapsulate repeatable sequences
- Return JSON from functions for pipeline consumption
- Use `$"..."` interpolation when composing JSON for `mkitem`
- Prefer `return` when producing values for downstream commands
