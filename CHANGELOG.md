# Changelog

## 1.1.114-preview — 2026-07-01

A large feature cycle on top of 1.1.4-preview. The shell gains a stack of new commands — configurable **color themes**, a native **`filter`** language, change-feed **`watch`**, **`index`** and **`throughput`** management, bulk **`import`/`export`**, and server-side **`sproc`/`udf`/`trigger`** programming — plus a reworked **`info`** command (formerly `settings`) with usage statistics and JSON output, first-class observability through **`--diagnostics`** and **`--otel`**, and hardening of the MCP server.

### Highlights

- **Configurable color themes.** A new `theme` command inspects, switches, loads, validates, saves, edits, and reloads shell color themes, with built-in profiles and user themes under `~/.cosmosdbshell/themes`. The validator collects every issue in a single pass and suggests the closest valid token on typos. ([#83](https://github.com/Azure/CosmosDBShell/pull/83), [#97](https://github.com/Azure/CosmosDBShell/pull/97))
- **Native `filter` command.** A small, shell-safe, jq-inspired expression language for filtering and reshaping JSON in the pipeline; results stay structured JSON so `filter` composes with later commands. For features outside the v1 grammar, pipe results to the separate external `jq` command. ([#67](https://github.com/Azure/CosmosDBShell/pull/67))
- **Change-feed `watch` (alias `tail`).** Tails a container's change feed, printing new and modified items as highlighted JSON, with `--from-beginning`, `--partition-key`, `--max`, and `--interval`. ([#115](https://github.com/Azure/CosmosDBShell/pull/115))
- **`index` and `throughput` management.** `index` manages a container's indexing policy with `show`/`add`/`remove`/`set` subcommands; `throughput` views and scales RU/s with `show`/`set`/`manual`/`autoscale`, value validation, and a confirmation prompt before billable changes. ([#116](https://github.com/Azure/CosmosDBShell/pull/116), [#130](https://github.com/Azure/CosmosDBShell/pull/130))
- **Bulk `import`/`export`.** Round-trip items to and from JSON Lines, JSON array, or CSV files, with streaming for JSON formats, `--mode=upsert`, `--continue-on-error`, `--dry-run`, and CSV partition-key nesting. ([#95](https://github.com/Azure/CosmosDBShell/pull/95))
- **Server-side programming.** New `sproc`, `udf`, and `trigger` commands manage stored procedures, user-defined functions, and triggers on the current container. ([#124](https://github.com/Azure/CosmosDBShell/pull/124))
- **`info` command with usage statistics.** The former `settings` command is renamed to `info` and now reports usage statistics — document counts, storage sizes, and throughput — alongside configuration, with `--partitions` for per-partition distribution, `--detailed` for storage and top-partition-key breakdowns, and machine-readable JSON via `--format json` or redirected output. ([#134](https://github.com/Azure/CosmosDBShell/pull/134), [#148](https://github.com/Azure/CosmosDBShell/pull/148))
- **Observability.** `--diagnostics [path]` writes timestamped diagnostic logs (commands, timing, errors, connection events); `--otel [endpoint]` enables W3C distributed tracing and optional OTLP export. ([#127](https://github.com/Azure/CosmosDBShell/pull/127), [#126](https://github.com/Azure/CosmosDBShell/pull/126))

### New features

- `theme` command to inspect, switch, load, validate, save, edit, open, and reload shell color themes, with strict validation (`--strict`) and a user themes directory. ([#83](https://github.com/Azure/CosmosDBShell/pull/83))
- `filter` command — a native jq-inspired JSON filter/transform language that keeps results structured in the pipeline. ([#67](https://github.com/Azure/CosmosDBShell/pull/67))
- `watch` command (also `tail`) to follow a container's change feed; not exposed over MCP because it is interactive and streaming. ([#115](https://github.com/Azure/CosmosDBShell/pull/115))
- `edit` command to open a local file in an external editor and wait for it to close, resolved from `$VISUAL`, then `$EDITOR`, then a platform default. ([#117](https://github.com/Azure/CosmosDBShell/pull/117), [#110](https://github.com/Azure/CosmosDBShell/issues/110))
- `index` command to manage container indexing policies through `show`/`add`/`remove`/`set` subcommands. ([#116](https://github.com/Azure/CosmosDBShell/pull/116))
- `throughput` command to view and scale provisioned RU/s through `show`/`set`/`manual`/`autoscale`, with RU/s validation and a confirmation prompt for billable changes. ([#130](https://github.com/Azure/CosmosDBShell/pull/130), [#109](https://github.com/Azure/CosmosDBShell/issues/109))
- `info` command (renamed from `settings`) reporting configuration and usage statistics: document count and data/total storage for a container, container/document/storage/throughput aggregates for a database, and the database count at the account root; `--partitions` shows the per-physical-partition document distribution, `--detailed` adds a storage breakdown and top partition keys, and `--format json` (or redirected output) emits machine-readable JSON. ([#134](https://github.com/Azure/CosmosDBShell/pull/134), [#148](https://github.com/Azure/CosmosDBShell/pull/148), [#108](https://github.com/Azure/CosmosDBShell/issues/108))
- `import` and `export` commands for bulk JSON Lines / JSON array / CSV round-trip. ([#95](https://github.com/Azure/CosmosDBShell/pull/95))
- `sproc` command to manage Cosmos DB for NoSQL stored procedures on the current container: `list`, `show`, `exists` (returns a boolean usable in `if`/`while` conditions), `create` (from a JavaScript file or piped body, with `--force` to replace), `exec` (with a JSON argument array and `--partition-key`), `edit` (interactive external editor), and `delete`. ([#124](https://github.com/Azure/CosmosDBShell/pull/124), [#103](https://github.com/Azure/CosmosDBShell/issues/103))
- `udf` command to manage Cosmos DB for NoSQL user-defined functions on the current container: `list`, `show`, `exists` (returns a boolean usable in `if`/`while` conditions), `create` (from a JavaScript file or piped body, or interactively in an external editor when no body is supplied, with `--force` to replace), `edit` (interactive external editor), and `delete`. ([#124](https://github.com/Azure/CosmosDBShell/pull/124), [#103](https://github.com/Azure/CosmosDBShell/issues/103))
- `trigger` command to manage Cosmos DB for NoSQL triggers on the current container: `list`, `show`, `exists` (returns a boolean usable in `if`/`while` conditions), `create` (from a JavaScript file or piped body, or interactively in an external editor when no body is supplied, with `--type` for pre/post, `--operation` for the operation, and `--force` to replace), `edit` (interactive external editor that preserves the trigger type and operation), and `delete`. ([#124](https://github.com/Azure/CosmosDBShell/pull/124), [#103](https://github.com/Azure/CosmosDBShell/issues/103))
- `--diagnostics [path]` startup option to capture timestamped diagnostic logs to a file, or to a timestamped file in the config directory by default. ([#127](https://github.com/Azure/CosmosDBShell/pull/127), [#122](https://github.com/Azure/CosmosDBShell/issues/122))
- `--otel [endpoint]` startup option to enable distributed tracing (sampled W3C `traceparent`) and optionally export spans to an OTLP endpoint, falling back to `OTEL_EXPORTER_OTLP_ENDPOINT`. ([#126](https://github.com/Azure/CosmosDBShell/pull/126))

### Improvements

- The REPL highlights incomplete constructs so unterminated input is visually distinct while you keep typing. ([#93](https://github.com/Azure/CosmosDBShell/pull/93))
- Hardcoded colors now route through the active `Theme`, and JSON output is highlighted by token position for more accurate coloring. ([#97](https://github.com/Azure/CosmosDBShell/pull/97))
- Unknown-command diagnostics show a source caret aligned under the offending token, including when the line is ellipsis-truncated. ([#99](https://github.com/Azure/CosmosDBShell/pull/99), [#96](https://github.com/Azure/CosmosDBShell/issues/96))
- Refreshed shell prompt with a chevron marker, the connected account name, and an explicit offline label. ([#133](https://github.com/Azure/CosmosDBShell/pull/133))
- `ls` prints a result-count summary for databases and containers ([#129](https://github.com/Azure/CosmosDBShell/pull/129)), and the summary line is now consistent across databases, containers, and items ([#139](https://github.com/Azure/CosmosDBShell/pull/139)).

### Security

- MCP tool-call hardening and transport security: tighter request handling and origin/transport validation for the HTTP MCP server. ([#120](https://github.com/Azure/CosmosDBShell/pull/120))
- Resolved CodeQL alerts SM05137 and SM02184 in the connect flow. ([#132](https://github.com/Azure/CosmosDBShell/pull/132))

### Fixes

- REPL syntax highlighting now covers every statement in `;`-separated multi-statement input instead of stopping at the first `;`, and colors the `;` separators with the operator color. ([#141](https://github.com/Azure/CosmosDBShell/pull/141))

### Breaking changes

- The `settings` command has been renamed to `info` and is no longer available under its old name. Update scripts and aliases that invoke `settings` to use `info` instead. ([#134](https://github.com/Azure/CosmosDBShell/pull/134), [#108](https://github.com/Azure/CosmosDBShell/issues/108))
- The standalone `indexpolicy` command has been removed and is now an alias of `index`. Its old grammar no longer works: use `indexpolicy show` (was `indexpolicy`) to display the policy and `indexpolicy set '<json>'` (was `indexpolicy '<json>'`) to replace it, or just use the `index` command, which also supports incremental `add`/`remove` and `--mode`/`--automatic` patches. ([#140](https://github.com/Azure/CosmosDBShell/pull/140))
- Removed the `--editor` option from `theme edit`. The external editor is now always resolved from `$VISUAL`, then `$EDITOR`, then a platform default — consistent with `sproc edit`, `udf edit`, and `trigger edit`. Set `$VISUAL` or `$EDITOR` to choose a specific editor.

### Documentation

- Updated contributing guidelines and expanded the README with build information. ([#123](https://github.com/Azure/CosmosDBShell/pull/123))

### Build & pipeline

- CI publishes code coverage to GitHub Code Quality on pull requests. ([#128](https://github.com/Azure/CosmosDBShell/pull/128))
- Expanded unit coverage for the parser and offline command paths. ([#121](https://github.com/Azure/CosmosDBShell/pull/121))

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
