# Filter Expression Language v1

This document defines the v1 grammar and semantics for the native `filter` command.

`filter` is a built-in JSON transformation command for CosmosDBShell. It uses a jq-inspired expression language, but it is not a jq implementation and does not attempt full jq compatibility.

## Goals

- Support common JSON shaping and filtering workflows inside CosmosDBShell.
- Preserve structured pipeline results so `filter` can feed later commands.
- Keep the language small enough to implement and document clearly.

## Non-Goals

- Full jq compatibility.
- jq modules, variables, function definitions, or streaming mode.
- Regex support in v1.
- Update-assignment operators such as `|=`, `+=`, `del`, or `setpath`.
- Exact jq multi-result generator semantics.

## Command Contract

The command surface for v1 is:

```text
filter <expression>
```

- `expression` is required.
- The expression is usually quoted at the shell level, for example: `filter '.items[0]'`.
- Input comes from the current pipeline value.
- Output is written back into the shell's structured command result.

## Data Model

The language operates on JSON values:

- `null`
- booleans
- numbers
- strings
- arrays
- objects

The current pipeline value is referred to as `.`.

## Evaluation Model

`filter` evaluates expressions eagerly.

- Expressions consume one input JSON value.
- Expressions produce one JSON value.
- Operations that would naturally produce multiple values in jq are materialized as arrays in v1 when needed for shell-safe behavior.
- Intermediate results remain JSON values and can be passed to downstream commands.

## Lexical Rules

### Whitespace

Whitespace may appear between tokens and is ignored unless it appears inside a string literal.

### Identifiers

Identifiers are used for object shorthand fields and builtin names.

```text
identifier = letter , { letter | digit | '_' }
```

Examples:

- `id`
- `items`
- `sort_by`

### Literals

Supported literals:

- `null`
- `true`
- `false`
- integer and decimal numbers
- double-quoted strings with JSON-style escapes

Examples:

- `null`
- `true`
- `42`
- `3.14`
- `"active"`

## Grammar

The grammar below uses a compact EBNF-style notation.

```text
filter-expression  = expression ;

expression         = pipe-expression ;

pipe-expression    = comparison-expression , { '|' , comparison-expression } ;

comparison-expression
                   = primary-expression , [ comparison-operator , primary-expression ] ;

comparison-operator
                   = '==' | '!=' | '<' | '<=' | '>' | '>=' ;

primary-expression = path-expression
                   | literal
                   | builtin-expression
                   | array-constructor
                   | object-constructor
                   | '(' , expression , ')' ;

path-expression    = '.' , { path-segment } ;

path-segment       = '.' , identifier , [ '?' ]
                   | '[' , integer-literal , ']' , [ '?' ]
                   | '[' , ']' , [ '?' ] ;

builtin-expression = 'length'
                   | 'keys'
                   | 'type'
                   | 'contains' , '(' , expression , ')'
                   | 'map' , '(' , expression , ')'
                   | 'select' , '(' , expression , ')'
                   | 'sort_by' , '(' , expression , ')' ;

array-constructor  = '[' , [ expression , { ',' , expression } ] , ']' ;

object-constructor = '{' , [ object-field , { ',' , object-field } ] , '}' ;

object-field       = identifier
                   | identifier , ':' , expression
                   | string-literal , ':' , expression ;
```

## Semantics

### Identity

`.` returns the current input unchanged.

Examples:

- `.`
- `. | type`

### Property Access

`.name` reads the property `name` from an object.

- If the input is an object and the property exists, the property value is returned.
- If the property does not exist, v1 returns `null`.
- If the input is not an object, evaluation fails unless optional access is used.

Examples:

- `.id`
- `.items`
- `.metadata.status`

### Optional Property Access

`.name?` behaves like `.name`, but it suppresses type/access errors.

- If the input is not an object, the result is `null`.
- If the property does not exist, the result is `null`.

### Array Index Access

`.[n]` reads the array element at zero-based index `n`.

- If the input is an array and the index exists, the element is returned.
- If the index is out of range, the result is `null`.
- If the input is not an array, evaluation fails unless optional access is used.

Examples:

- `.[0]`
- `.items[0]`

### Optional Array Index Access

`.[n]?` suppresses type/access errors and returns `null` when the access cannot be satisfied.

### Array Iteration

`.[]` iterates the values of an array.

In v1, iteration is materialized for shell-safe semantics:

- if the input is an array, the result is an array containing the iterated values
- if the input is an object, `.[]` is not supported in v1 unless object iteration is explicitly added later
- if the input is not an array, evaluation fails unless optional iteration is used

Examples:

- `.items[]`
- `[.items[] | .id]`

### Optional Iteration

`.[]?` returns `null` when the input is not an array.

### Pipe

`a | b` evaluates `a` first, then evaluates `b` against the result of `a`.

Examples:

- `.items | length`
- `.items | map(.id)`
- `.items[0] | {id, status}`

### Comparison

Comparisons produce booleans.

Supported operators:

- `==`
- `!=`
- `<`
- `<=`
- `>`
- `>=`

Examples:

- `.status == "active"`
- `.count > 10`

### Array Construction

`[expr1, expr2, ...]` evaluates each expression against the current input and constructs a JSON array from the results.

Examples:

- `[.id, .status]`
- `[.items[0], .items[1]]`

### Object Construction

`{...}` constructs a JSON object.

Supported forms:

- shorthand field capture: `{id, status}`
- explicit mapping: `{id: .id, state: .status}`
- string keys: `{"item-id": .id}`

For shorthand fields, `{id}` is equivalent to `{id: .id}`.

## Builtins

### `length`

Returns the length of the input value.

- array: number of elements
- object: number of properties
- string: number of characters
- null: `0`
- number and boolean: runtime error in v1

Examples:

- `.items | length`
- `.name | length`

### `keys`

Returns an array of object property names.

- input must be an object
- result ordering should be deterministic

Example:

- `.item | keys`

### `type`

Returns one of the strings:

- `"null"`
- `"boolean"`
- `"number"`
- `"string"`
- `"array"`
- `"object"`

Example:

- `.payload | type`

### `contains(expr)`

Evaluates `expr` against the current input and returns whether the input contains the resulting value.

v1 behavior:

- string contains string substring
- array contains element by JSON equality
- object contains object subset by matching keys and values
- other types use JSON equality

Examples:

- `.tags | contains("prod")`
- `.item | contains({status: "active"})`

### `map(expr)`

Applies `expr` to each element of the input array and returns an array of transformed values.

- input must be an array
- each element becomes the current input while evaluating `expr`

Examples:

- `.items | map(.id)`
- `.items | map({id, status})`

### `select(expr)`

Filters an input array by applying `expr` to each element and keeping elements where the result is `true`.

- input must be an array
- `expr` is evaluated per element
- only boolean `true` keeps an element in v1

Examples:

- `.items | select(.status == "active")`
- `.items | select(.count > 10)`

### `sort_by(expr)`

Sorts an input array using the value produced by `expr` for each element.

- input must be an array
- keys must be mutually comparable
- stable sorting is preferred

Examples:

- `.items | sort_by(.id)`
- `.items | sort_by(.timestamp)`

## Type Rules

- Property access requires an object unless optional access is used.
- Index access and array builtins require an array unless optional access is used.
- `map`, `select`, and `sort_by` require arrays.
- `keys` requires an object.
- `length` supports arrays, objects, strings, and null.
- Comparisons require values that the implementation can compare deterministically.

## Error Behavior

v1 should distinguish these error classes:

### Parse Errors

Invalid syntax in the expression.

Examples:

- `.items[`
- `{id: }`

### Unsupported Feature Errors

Syntax that looks jq-like but is outside the v1 contract.

Examples:

- `.items[] | .id, .status`
- `reduce .items[] as $x (...)`
- `.name |= "x"`
- `test("abc")`

The diagnostic should say the construct is not supported by `filter` v1 and should suggest using `jq` when full jq behavior is required.

### Runtime Type Errors

The expression is syntactically valid but is applied to the wrong input shape.

Examples:

- `.id` applied to an array
- `map(.id)` applied to an object

## Supported Examples

```text
.
.items[0]
.items[0]?.id
.items | length
.items | map(.id)
.items | select(.status == "active")
.items | sort_by(.id)
.items | map({id, status})
{id, status}
[.id, .status]
```

## Unsupported Examples

```text
.[] | .id, .status
reduce .items[] as $item (0; . + $item)
.items |= map(.id)
def pickId: .id; pickId
test("^abc")
inputs
```

## Implementation Notes

The intended implementation model for v1 is:

- parse into a small AST
- evaluate against `JsonElement`
- materialize iteration results as arrays where needed
- store the final JSON value back into `CommandState.Result`

This language should be implemented as a native shell feature, not as a compatibility layer over the external `jq` executable.

## Open Questions For Later Versions

- Should object iteration with `.[]` be supported?
- Should `select(expr)` accept jq-like truthiness or only strict `true`?
- Should negative array indices be supported?
- Should array slicing be added?
- Should string helper functions be added before regex support?
- Should multi-result semantics be expanded beyond array materialization?