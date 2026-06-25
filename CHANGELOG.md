# Changelog

## 1.1.100-preview — 2026-06-25

### New features

- `sproc` command to manage Cosmos DB for NoSQL stored procedures on the current container: `list`, `show`, `exists` (returns a boolean usable in `if`/`while` conditions), `create` (from a JavaScript file or piped body, with `--force` to replace), `exec` (with a JSON argument array and `--partition-key`), `edit` (interactive external editor), and `delete`. ([#103](https://github.com/Azure/CosmosDBShell/issues/103))
- `udf` command to manage Cosmos DB for NoSQL user-defined functions on the current container: `list`, `show`, `exists` (returns a boolean usable in `if`/`while` conditions), `create` (from a JavaScript file or piped body, or interactively in an external editor when no body is supplied, with `--force` to replace), `edit` (interactive external editor), and `delete`. ([#103](https://github.com/Azure/CosmosDBShell/issues/103))
- `trigger` command to manage Cosmos DB for NoSQL triggers on the current container: `list`, `show`, `exists` (returns a boolean usable in `if`/`while` conditions), `create` (from a JavaScript file or piped body, or interactively in an external editor when no body is supplied, with `--type` for pre/post, `--operation` for the operation, and `--force` to replace), `edit` (interactive external editor that preserves the trigger type and operation), and `delete`. ([#103](https://github.com/Azure/CosmosDBShell/issues/103))

### Breaking changes

- The standalone `indexpolicy` command has been removed and is now an alias of `index`. Its old grammar no longer works: use `indexpolicy show` (was `indexpolicy`) to display the policy and `indexpolicy set '<json>'` (was `indexpolicy '<json>'`) to replace it, or just use the `index` command, which also supports incremental `add`/`remove` and `--mode`/`--automatic` patches.
- Removed the `--editor` option from `theme edit`. The external editor is now always resolved from `$VISUAL`, then `$EDITOR`, then a platform default — consistent with `sproc edit`, `udf edit`, and `trigger edit`. Set `$VISUAL` or `$EDITOR` to choose a specific editor.

## 1.1.4-preview — 2026-05-21

First release on the 1.1 line. A pretty packed cycle. The headline change is **ARM-based control plane for database and container management**, but there’s also a fully reworked CLI, two new item commands, a much friendlier shell experience for newcomers, and a long list of paper-cut fixes.

### Highlights

- **Database and container operations now go through Azure Resource Manager.** `mkdb`, `mkcon`, `rmdb`, `rmcon`, `settings`, and `indexpolicy` use ARM when the connection includes a token credential, and fall back to the data plane when it doesn’t (account key, `COSMOSDB_SHELL_TOKEN`, emulator). This means the shell respects RBAC role assignments for control-plane actions instead of relying on master keys, and works on accounts where data-plane management is restricted. `--subscription` and `--resource-group` let you target an account explicitly; otherwise the shell tries to discover the matching ARM account from the credential. ([#75](https://github.com/Azure/CosmosDBShell/pull/75))
- **CLI parser migrated from CommandLineParser to System.CommandLine.** Better error messages for unknown args, proper handling of `-c "command with spaces"` and `-k "raw command"`, and consistent behavior for `--help`, `--version`, and `--lsp`. ([#72](https://github.com/Azure/CosmosDBShell/pull/72))
- **`replace` and `patch` item commands.** `replace` updates an existing item from JSON (deriving id and partition key from the JSON, with `--etag` for optimistic concurrency). `patch` applies a single Cosmos patch operation — `set`, `add`, `replace`, `remove`, or `incr` — against a field path on an item identified by id and partition key. No more round-tripping through `print` + `mkitem`. ([#71](https://github.com/Azure/CosmosDBShell/pull/71))
- **Syntax highlighting in the REPL.** JSON command output gets colorized, and matching `()` `[]` `{}` are coloured by nesting depth (rainbow brackets). ([#80](https://github.com/Azure/CosmosDBShell/pull/80))
- **Multi-line REPL input.** Continue a statement on the next line by ending it with `\`, or just keep typing — the parser detects incomplete input (unbalanced braces, unterminated strings, dangling operators) and shows a continuation prompt automatically. Recalled history entries replay across the same number of lines. ([#88](https://github.com/Azure/CosmosDBShell/pull/88))
- **Parser and query diagnostics with line, column, and source caret.** Errors are localized, point at the offending token with a `^` caret view, identify the script file when running `-f`, and suggest the closest command or option name on typos (“Did you mean…”). Stack traces are no longer dumped for runtime errors. ([#87](https://github.com/Azure/CosmosDBShell/pull/87))
- **Interactive keyboard shortcuts.** Bindings for common navigation and editing actions in the REPL. ([#57](https://github.com/Azure/CosmosDBShell/pull/57))
- **Friendlier first run.** When the shell starts without a connection — or when `connect` is run with no arguments — it now prints a short usage hint instead of a bare prompt. ([#82](https://github.com/Azure/CosmosDBShell/pull/82))

### New features

- New `connect` options `--subscription` and `--resource-group` (and their startup counterparts `--connect-subscription`, `--connect-resource-group`) to explicitly target an ARM Cosmos DB account.
- `connect` now displays an “ARM Account” row when an ARM context is attached.
- Sovereign-cloud aware ARM endpoint resolution: known cloud table for Public / China / US Gov / Germany, plus a `login.X` → `management.X` fallback for additional national clouds. ([#75](https://github.com/Azure/CosmosDBShell/pull/75))
- `replace` and `patch` item commands. ([#71](https://github.com/Azure/CosmosDBShell/pull/71))
- JSON output syntax highlighting and depth-cycled bracket coloring. ([#80](https://github.com/Azure/CosmosDBShell/pull/80))
- Multi-line REPL input with `\` line-continuation and parser-driven incomplete-input detection; continuation prompt on subsequent rows including history recall. ([#88](https://github.com/Azure/CosmosDBShell/pull/88))
- Parser/query diagnostics show line, column, source line with caret, and “Did you mean…” suggestions for unknown commands and options. ([#87](https://github.com/Azure/CosmosDBShell/pull/87))
- Interactive shell keyboard shortcuts. ([#57](https://github.com/Azure/CosmosDBShell/pull/57))
- Startup usage hint when disconnected. ([#82](https://github.com/Azure/CosmosDBShell/pull/82))

### Improvements

- `ls` pushes `SELECT TOP n` down to the server when no client-side filter is in play, so listing large containers no longer pulls the whole result set. ([#70](https://github.com/Azure/CosmosDBShell/pull/70))
- `ls` correctly displays hierarchical partition keys ([#64](https://github.com/Azure/CosmosDBShell/pull/64)) and is resilient when items have missing content streams ([#63](https://github.com/Azure/CosmosDBShell/pull/63)).
- `cd` now rejects paths that try to descend below `/database/container`. ([#69](https://github.com/Azure/CosmosDBShell/pull/69))
- Entra interactive sign-in attempts are cancellable, so a `connect` that opens a browser tab can be aborted with `Ctrl+C`. ([#62](https://github.com/Azure/CosmosDBShell/pull/62))
- Emulator connection failures produce a clearer, actionable error message. ([#84](https://github.com/Azure/CosmosDBShell/pull/84))
- `--help` / `/?` output reflowed for readability, and all remaining help strings are localized.
- New long option spellings `--clear-history` and `--color-system` (the unhyphenated forms still work).
- `settings` now validates the database/container before fetching, so missing resources produce the standard localized `database_not_found` / `container_not_found` message regardless of whether the call routes through ARM or the data plane.

### Fixes

- `connect` no longer regresses to a failure when the credential has no ARM access — it falls back to the data plane cleanly. ([#75](https://github.com/Azure/CosmosDBShell/pull/75))
- Token-credential connect paths properly dispose the `CosmosClient` when ARM completion fails, so a failed connect never leaks a half-initialized client.
- Data-plane container reads guard against null `Container.Resource` responses.
- VS Code credential is reused correctly when `connect` is re-issued in the same session. ([#73](https://github.com/Azure/CosmosDBShell/pull/73))
- Highlighter no longer duplicates text inside interpolated strings, and lexes interpolated-string interiors with accurate outer-source positions.
- `PrintConnectUsageHint` escapes the localized header/footer so they render correctly with markup-bearing values.

### Documentation

- New “telemetry” section in [README](README.md) describing what data the shell collects, with explicit clarification of what is and isn’t collected around Entra ID authentication. ([#78](https://github.com/Azure/CosmosDBShell/pull/78))
- [docs/connect.md](docs/connect.md), [docs/commands.md](docs/commands.md), [docs/navigation.md](docs/navigation.md), and [docs/mcp.md](docs/mcp.md) updated for the new ARM options, the strict-RBAC limitation of key-based connections, and the four-step ARM endpoint resolution order.
- [docs/navigation.md](docs/navigation.md) and [README](README.md) document multi-line REPL input. ([#88](https://github.com/Azure/CosmosDBShell/pull/88))

### Build & pipeline

- Official pipeline now zips signed per-RID publish folders so downloadable artifacts are ready to use. ([#77](https://github.com/Azure/CosmosDBShell/pull/77))
- Artifact upload trims `out\` to `zip`+`nupkg` only; expected exe is matched by file name with project casing.
- Versioning moved to [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning). Major/Minor and the prerelease label come from `version.json`; the patch is the git height since that file last changed, so a Major/Minor bump cleanly resets the patch to 0. Local `dotnet build` now produces the same version as CI (previously local builds stamped `1.0.0`). The redundant `/p:Version=…`, `/p:FileVersion=…`, `/p:InformationalVersion=…`, and `/p:PackageVersion=…` overrides were removed from the GitHub Actions and OneBranch pipelines. ([#90](https://github.com/Azure/CosmosDBShell/pull/90), [#91](https://github.com/Azure/CosmosDBShell/pull/91))
