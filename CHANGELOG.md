# Changelog

## Unreleased

### Fixes

- Local emulator outages are now detected across Cosmos DB commands. Requests fail promptly with an error and return the shell to its disconnected state instead of leaving an unresponsive session labeled as connected.

## 1.1.209-preview — 2026-08-26

### New features

- **`schema` discovery command and MCP tool.** A new read-only `schema` command infers a container's structure from a small, bounded sample: it returns the partition key path(s), an indexing policy summary, an estimated document count, and inferred field types (with dot notation for nested objects and per-field presence counts). `--sample <n>` selects how many documents to sample (clamped to 1-100, default 20), `--fields-only` (alias `--short`) returns only the sample count and inferred fields without a metadata read, and `--database`/`--container` override the target. Exposed as a read-only MCP tool so agents can discover container structure cheaply instead of re-sampling or guessing field names. ([#160](https://github.com/Azure/CosmosDBShell/issues/160))
- **`--database` and `--container` startup options.** Navigate to a database or container at startup without composing a `-k "cd ..."` command. Both require `--connect`, and `--container` requires `--database`. Tools that previously built a startup script string to select a location should pass these options instead. See [navigation](docs/navigation.md).

### Breaking changes

- **String interpolation now requires the explicit `$"..."` prefix.** In ordinary double-quoted strings, `$name` and `$(...)` are literal text and are no longer evaluated — previously any `"..."` string containing `$` was interpolated. Scripts that relied on `"Hello $name"` must be changed to `$"Hello $name"`; this change is silent, so review scripts that build strings from `$` values. Inside an interpolated string, `$(...)` evaluates a complete expression, but command expressions are rejected; `\$` escapes a literal dollar sign. Ordinary double-quoted strings additionally accept `\uXXXX` escapes. See [programming](docs/programming.md).

### Fixes

- Command text reconstructed from a parsed command (MCP tool-call history, the echoed command line, and AST `ToString()`) is now serialized through a single literal writer. Values were previously quoted only when they contained a space, and were never escaped, so a value containing a quote, a backslash, a semicolon, or a control character produced command text that no longer parsed back to the original value. Options also lost their `-` prefix and their value separator. Reconstructed command text now round-trips.

## 1.1.190-preview — 2026-08-25

### New features

- **`whoami` and `can-i` access diagnostics.** `whoami` reports the current credential type and, for Microsoft Entra ID connections, the principal, tenant, application id, user principal name, display name, and token expiry decoded from the Cosmos DB access token. `can-i <read|query|write|manage>` probes data-plane access with safe, non-mutating requests and reports `allow`, `deny`, or `indeterminate`. Both commands are data-plane only (no control-plane dependency): account-key and emulator connections are reported from the master key, and RBAC role assignments are not enumerated. Both support `--format` (`table`, `json`, or `csv`). ([#163](https://github.com/Azure/CosmosDBShell/issues/163))
- **Deterministic machine output and exit codes.** Global `--output`/`--quiet`, structured JSON/CSV machine mode, and stable process exit codes (`0`–`6`) for automation and CI. ([#173](https://github.com/Azure/CosmosDBShell/pull/173), [#155](https://github.com/Azure/CosmosDBShell/issues/155), [#176](https://github.com/Azure/CosmosDBShell/issues/176), [#177](https://github.com/Azure/CosmosDBShell/issues/177))
- **`setup-cosmosdb-shell` GitHub Action** and [CI/CD guide](docs/ci.md) for installing the self-contained shell in pipelines without a .NET SDK on the runner. ([#173](https://github.com/Azure/CosmosDBShell/pull/173))

### Improvements

- **Destructive MCP commands now prompt for confirmation instead of being blocked.** When an MCP client invokes `delete`, `rm`, `rmcon`, or `rmdb`, the server sends an elicitation prompt describing the exact command line and only runs it if the user approves; a declined prompt, a cancelled prompt, or a client without confirmation support results in nothing being executed. ([#183](https://github.com/Azure/CosmosDBShell/pull/183), [#158](https://github.com/Azure/CosmosDBShell/issues/158))
- **Preview releases are clearly identified at startup.** The welcome screen and compact startup output now show the current preview version and warn that commands, output, and behavior may change before general availability. ([#193](https://github.com/Azure/CosmosDBShell/pull/193))

### Breaking changes

- Failures no longer always exit with `1`. Exit codes are now classified into a stable set — `2` usage/parse errors, `3` authentication, `4` connection, `5` not found, `6` throttled — with `1` reserved for uncategorized failures. Scripts that branch on the exact value `1` must be updated; checks for a non-zero exit code are unaffected. See the [exit-code contract](docs/ci.md). ([#173](https://github.com/Azure/CosmosDBShell/pull/173), [#176](https://github.com/Azure/CosmosDBShell/issues/176))
- Execute-and-quit (`-c`) now defaults to machine mode with JSON output instead of the interactive human-facing view. ANSI colors, banners, and informational messages are suppressed, and errors are written to STDERR as `{"status":"error","error":"..."}`. Pass `--output user` or `--output table` to restore the previous presentation. ([#173](https://github.com/Azure/CosmosDBShell/pull/173), [#155](https://github.com/Azure/CosmosDBShell/issues/155))
- The structured JSON emitted by commands now follows a consistent contract. Listings return `{ "type": "<kind>", "values": [ ... ] }` — this replaces the bare arrays previously returned by `dir`, `sproc list`, `udf list`, and `trigger list`, the `{ "items": [...] }` envelope used by `ls`, `query`, and `watch`, and the `{ "themes": [...] }` envelope used by `theme list`. `query` no longer switches between `items` and `documents` depending on whether metrics were requested. Single-resource results return `{ "type": "<kind>", "id": "<name>", "<verb>": true }`, so `mkdb`/`mkcon` no longer return `created_database`/`created_container`, `rmdb` reports `id` instead of `db`, `rmcon` reports `id` instead of `container`, both always include `dryRun`, and `sproc`/`udf`/`trigger create` now report `created` on success instead of omitting it. `theme` subcommands report the theme in `id` with a boolean verb (`active`, `applied`, `previewed`, `loaded`, `saved`, `edited`, `opened`) rather than encoding the name in the key. `cd` and `pwd` return `{ "type": "location", "database": ..., "container": ..., "currentLocation": ... }` — `cd` no longer returns the `"connected state"` / `"database state"` / `"container state"` keys. `connect` and `disconnect` return a `"type": "connection"` payload, and a successful `connect <endpoint>` returns `{ "connected": true, "endpoint": "..." }` instead of `{ "connected state": "..." }`. Item listings additionally carry a `limitReached` flag. Tooling that parses this output must be updated. ([#173](https://github.com/Azure/CosmosDBShell/pull/173))
- Write commands report what they actually did instead of `{ "result": "success" }`. `mkitem`/`create item` return `{ "type": "item", "created": <n>, "replaced": <n>, "failed": <n>, "requestCharge": <ru> }`, `replace` returns `replaced`/`failed`/`requestCharge`, `patch` returns `{ "id": ..., "patched": true, "requestCharge": <ru> }`, `import` returns `{ "file": ..., "imported": <n>, "failed": <n>, "requestCharge": <ru>, "dryRun": <bool> }`, and `export` returns `{ "file": ..., "exported": <n>, "requestCharge": <ru> }`. ([#173](https://github.com/Azure/CosmosDBShell/pull/173))

### Fixes

- `cd` and `mkdb` built their JSON result by string concatenation, so a database or container name containing a double quote produced malformed JSON and failed the command instead of navigating or reporting the created database. Both now serialize the payload properly. ([#173](https://github.com/Azure/CosmosDBShell/pull/173))
- `theme list` rendered a single `themes` column containing the whole JSON array when `--output csv` or `--output table` was used, because the shared table renderer only recognized the `values`/`items` list envelopes. It now emits one row per theme. ([#173](https://github.com/Azure/CosmosDBShell/pull/173))
- `theme list` and `theme show` wrote their human-facing output while the command executed, so `--output table` printed the interactive listing *and* the rendered table. Both now defer that output to the interactive renderer. ([#173](https://github.com/Azure/CosmosDBShell/pull/173))

### Build & pipeline

- **NuGet packages are now framework-dependent.** The primary `CosmosDBShell` .NET tool package uses the installed .NET 10 runtime for a smaller download, while standalone self-contained builds continue to ship as per-RID ZIP archives. ([#191](https://github.com/Azure/CosmosDBShell/pull/191), [#174](https://github.com/Azure/CosmosDBShell/issues/174))
- Removed the redundant advanced CodeQL workflow and made the repository's GitHub-managed CodeQL default setup the single source of code-scanning results. ([#194](https://github.com/Azure/CosmosDBShell/pull/194))

## 1.1.150-preview — 2026-07-31

A short cycle on top of 1.1.136-preview. First interactive startup now shows a welcome screen (replayable via a new `welcome` command) with a more compact startup banner; `connect` gains a deterministic `--azure-cli` credential alongside clearer failure diagnostics and hardened credential selection; and the last direct `Newtonsoft.Json` usages are replaced with `System.Text.Json`.

### New features

- First-run **welcome screen** and `welcome` command. The embedded welcome screen is shown on the first interactive startup and can be redisplayed at any time with the new `welcome` command. ([#181](https://github.com/Azure/CosmosDBShell/pull/181))
- **`--azure-cli` / `--connect-azure-cli` credential option.** Selects `AzureCliCredential` directly, using the identity from your current `az login` session. It is slotted just above `DefaultAzureCredential` in the credential decision tree so environments with a live managed-identity/IMDS endpoint (for example Azure Cloud Shell) no longer silently authenticate as the managed identity — which often lacks Cosmos DB data-plane RBAC — instead of the interactive user. ARM context is attached like the other Entra ID flows, and `--tenant` is honored when supplied. ([#187](https://github.com/Azure/CosmosDBShell/pull/187))

### Improvements

- **Clearer connection failures.** When a connection fails, the shell now prints the underlying reason (the inner exception chain) in addition to the high-level "Failed to connect to the Cosmos DB account." message, and hints that `--verbose` shows full exception details including the stack trace. The startup `--connect` path previously printed only the top-level message. The shell also announces when a key is sourced from the `COSMOSDB_SHELL_ACCOUNT_KEY` environment variable, matching the existing `COSMOSDB_SHELL_TOKEN` behavior. ([#187](https://github.com/Azure/CosmosDBShell/pull/187))
- **Richer `--verbose` connection diagnostics.** In verbose mode, connection failures now surface the Cosmos DB request coordinates up front — HTTP status and sub-status codes plus the activity id — so an authorization denial (`403`) can be told apart from a token-acquisition failure or a network problem at a glance, followed by the full exception chain (including the `CosmosException` body/diagnostics and, for `DefaultAzureCredential`, the aggregated per-credential failure reasons). ([#187](https://github.com/Azure/CosmosDBShell/pull/187))
- **Conflicting credential selections are rejected.** Requesting two explicit credentials at once (for example `--connect-vscode-credential` together with `--azure-cli`, or a credential flag alongside an account key or `--managed-identity`) now fails with a clear message instead of silently ignoring one of them. `--azure-cli --tenant` reliably reaches the Azure CLI credential rather than falling into the interactive browser flow. ([#187](https://github.com/Azure/CosmosDBShell/pull/187))
- **Compact startup output.** Recurring startup text is replaced with a single compact version line and an MCP status line, and the report URL and disconnected warning no longer appear during normal startup. ([#181](https://github.com/Azure/CosmosDBShell/pull/181))

### Fixes

- Query index-metrics display now renders the utilized and potential index tables correctly. The `is JsonElement` checks in the metrics display path were previously dead — under `Newtonsoft.Json` the parsed values were `JObject`/`JValue` and never matched — and now match after the switch to `System.Text.Json`. ([#188](https://github.com/Azure/CosmosDBShell/pull/188))

### Build & pipeline

- Replaced the remaining direct `Newtonsoft.Json` usages with `System.Text.Json` and dropped the direct package reference (the Cosmos client is already configured to use `System.Text.Json`). Indexing-policy serialization preserves the existing output contract (camelCase `indexingMode`, `Consistent` enum value). ([#188](https://github.com/Azure/CosmosDBShell/pull/188))

## 1.1.136-preview — 2026-07-20

A focused cycle on top of 1.1.115-preview. New `ttl` and `conflict` commands manage container time-to-live and conflict-resolution policy; the `bucket` command gains control-plane throughput bucket limits; `--dry-run` previews land for `throughput` write subcommands and the destructive delete commands; and MCP tool results now emit structured JSON content. Rounding out the cycle are MCP connectivity fixes for agent clients and CI/pipeline hardening.

### New features

- `ttl` command to view and change a container's time-to-live policy: `ttl show` displays the current configuration as JSON (`disabled`, `no-default`, or `enabled`), `ttl set <seconds>` enables TTL with a positive default expiration, `ttl on` enables TTL with no container default (only items with their own `ttl` expire), and `ttl off` disables it. Targets the current container by default, with `--database`/`--container` overrides. ([#151](https://github.com/Azure/CosmosDBShell/pull/151), [#111](https://github.com/Azure/CosmosDBShell/issues/111))
- `conflict` command to view and change a container's conflict resolution policy: `conflict show` displays the policy as JSON, and `conflict set --mode <lastWriterWins|custom>` sets the mode with `--path` for last-writer-wins (defaults to `/_ts`) or `--procedure` for custom mode; unsupplied options keep their current value. Targets the current container by default, with `--database`/`--container` overrides. ([#151](https://github.com/Azure/CosmosDBShell/pull/151), [#111](https://github.com/Azure/CosmosDBShell/issues/111))
- `bucket` command now manages container throughput bucket limits in addition to client-side bucket selection. `bucket show` lists the throughput bucket limits configured on the current container, `bucket set <1-5> <1-100>` limits a bucket to a maximum percentage of the container's throughput, and `bucket clear <1-5>` removes a bucket's limit. These control-plane subcommands target the current container (or `--container`) and require an Azure AD (Entra) connection; the existing client-side `bucket`, `bucket <1-5>`, and `bucket 0` selection continues to work on any connection. ([#144](https://github.com/Azure/CosmosDBShell/issues/144))
- `throughput` write subcommands (`set`/`manual`/`autoscale`) now accept `--dry-run` to preview the change — reporting current vs. planned mode and RU/s as JSON (and a table interactively) — without applying it or prompting for confirmation. A first slice of dry-run mode (item G1). ([#164](https://github.com/Azure/CosmosDBShell/issues/164))
- **`--dry-run` for destructive delete commands.** `rm`, `rmcon`, `rmdb`, and `delete` accept `--dry-run` to preview the effect without deleting anything: `rm` reports how many items match the pattern, and `rmcon`/`rmdb` report the container or database that would be removed. No confirmation prompt is shown and no changes are made. ([#156](https://github.com/Azure/CosmosDBShell/issues/156))

### Improvements

- **Structured (JSON) tool results for MCP.** MCP tool results now carry the machine-readable JSON payload (`result`/`outputText`/`error` plus `currentLocation`) as first-class `structuredContent` in addition to the existing JSON text block, so agents can consume structured results directly. The two representations are kept byte-for-byte equivalent, and text-only clients are unaffected. ([#154](https://github.com/Azure/CosmosDBShell/issues/154))

- **Destructive MCP commands now prompt for confirmation instead of being blocked.** When an MCP client invokes `delete`, `rm`, `rmcon`, or `rmdb`, the server sends an elicitation prompt describing the exact command line and only runs it if the user approves; declining, cancelling, or a client that cannot confirm results in nothing being executed. This removes the need for any write opt-in flag. ([#158](https://github.com/Azure/CosmosDBShell/issues/158))

### Fixes

- MCP clients that reject unknown protocol versions (for example, Claude Code) can now connect: the server no longer advertises an unsupported protocol version. ([#150](https://github.com/Azure/CosmosDBShell/pull/150))
- MCP tool calls no longer fail with `ObjectDisposedException: The CancellationTokenSource has been disposed` when the shell cancels a prompt, so agents can invoke tools such as `ls` and `connect` reliably. ([#150](https://github.com/Azure/CosmosDBShell/pull/150))

### Build & pipeline

- Added a CodeQL analysis workflow for C# that builds the solution with `build-mode: manual`, complementing the repository's existing CodeQL default setup. ([#165](https://github.com/Azure/CosmosDBShell/pull/165))
- Updated GitHub Actions (`actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`) to versions that run on Node 24, resolving the Node 20 deprecation warnings. ([#152](https://github.com/Azure/CosmosDBShell/pull/152))
- Added a `union` merge driver for `CHANGELOG.md` via `.gitattributes` so concurrent PRs that each append an Unreleased entry merge automatically instead of conflicting. ([#172](https://github.com/Azure/CosmosDBShell/pull/172))

## 1.1.115-preview — 2026-07-01

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
