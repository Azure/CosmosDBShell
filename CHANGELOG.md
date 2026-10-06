# Changelog

## Unreleased

### New features

- MCP stdio mode no longer loads or records shell history; existing history files are left untouched. Explicit diagnostic logging remains available.
- Add `--mcp-stdio` for client-owned, headless MCP processes with protocol-only stdin/stdout, stderr diagnostics, no HTTP listener, and shutdown on stdin EOF. Startup connection/navigation, MCP confirmations, and location subscriptions are supported. Unsupported commands, including `jq`, `theme`, and interactive-only commands, are omitted from tools and help; destructive commands remain available through confirmation.
- MCP stdio mode never opens a browser for sign-in. `--tenant`/`--hint` connections use device code sign-in with instructions on stderr, and endpoint-only connections use `DefaultAzureCredential` without its interactive browser step. Cancelling the tool call ends a pending sign-in.

### Improvements

- Bound MCP stdio startup connection and navigation to 60 seconds and cancel pending startup when stdin closes. Report startup timeout errors on stderr with exit code 4. MCP request cancellation no longer logs an execution failure.
- Centralize shell presentation by message category. `--quiet` suppresses informational messages, progress, banners, and command echoes while preserving results, warnings, errors, and required authentication instructions, including previews shown before a confirmation. Machine-mode diagnostics use stderr without ANSI styling, and machine-mode stdout carries only the command result. MCP stdio continues to return results only through the protocol. Explicit diagnostic logging is unchanged.
- In machine mode, `theme`, `help`, and `edit` failures are now reported as structured `{ "status": "error", ... }` objects on stderr; previously they exited with code 1 and no message. Interactive and script errors from these commands are reported once.
- A new build analyzer rule (CZ0003) rejects direct `Console`/`AnsiConsole` output outside the shell's output policy.

## 1.1.271-preview — 2026-10-02

### New features

- **Transactional `batch` command.** Execute 1-100 `create`, `upsert`, `replace`, `delete`, or `patch` operations atomically within one partition key with `batch run <json> --partition-key <pk>`. Interactive shells can also assemble a batch with `begin`/`add`/`execute`, inspect it with `status`/`show`, or discard it with `cancel`. A failed operation rolls back the entire batch. MCP exposes only the one-shot `run` mode because clients share shell state. ([#135](https://github.com/Azure/CosmosDBShell/pull/135))
- **`query --explain`.** Inspect index usage, utilized and potential indexes, index-hit ratio, request charge, and a plain-language evaluation instead of returning documents. The probe reads only the first query page with `MaxItemCount = 1`; its metrics are estimates, and unavailable index metrics are reported as unknown rather than as a full scan. Available in interactive, machine-readable, and MCP output. ([#131](https://github.com/Azure/CosmosDBShell/pull/131))
- **Read-only `doctor` diagnostics.** Run bounded local, DNS, data-plane, and optional ARM checks without changing connection or navigation or initiating interactive login. Supports explicit database/container targets, an opt-in constant-projection query, timeout controls, and structured reports with verdicts, timings, and observed RUs. `doctor who` adds known credential and scope information without claiming verified write access. Clock-skew estimates reuse existing response headers; a bounded public GitHub release lookup reports available updates and can be disabled with `--no-update-check`. ([#209](https://github.com/Azure/CosmosDBShell/pull/209))
- **Live MCP shell-location resource.** Read `cosmos://shell/current-location` for both `currentLocation` and `currentAccountEndpoint`, and subscribe to updates caused by interactive or MCP navigation and connection changes. MCP `2026-07-28` clients use `subscriptions/listen`; clients using the `initialize` handshake use `resources/subscribe` and the session's GET stream. Notifications signal clients to reread the resource; explicit database/container arguments remain the reliable way to target independent operations. ([#223](https://github.com/Azure/CosmosDBShell/pull/223), [#231](https://github.com/Azure/CosmosDBShell/pull/231), [#232](https://github.com/Azure/CosmosDBShell/pull/232))

### Improvements

- Cosmos DB data-plane commands now consistently expose their aggregate observed request charge in structured output and connection-scoped `info` telemetry, including metadata/configuration operations, scripts, change feed reads, paginated operations, handled probes, and charged failures. MCP results carry the observed cost in a top-level `requestCharge` field, including charged failures. Azure Resource Manager control-plane operations remain uncharged. ([#171](https://github.com/Azure/CosmosDBShell/pull/171))
- Added `$sessionRequestCharge` and `$sessionChargedOperationCount` as read-only shell variables. Set `$sessionRequestChargeWarningThreshold` to a positive RU threshold to print one warning when the current connection reaches it; `info` reports it as `session.requestChargeWarningThreshold`. ([#171](https://github.com/Azure/CosmosDBShell/pull/171))
- MCP `query` and container-item `ls` calls now return bounded, resumable pages with an opaque `continuationToken`. Pass a non-null token back as `continuation` with the same query and options to retrieve the next page. Database/container name listings remain complete, and interactive and scripted commands retain their existing multi-page behavior. ([#204](https://github.com/Azure/CosmosDBShell/pull/204))
- The HTTP MCP server supports protocol `2026-07-28` without a session, including streamed `subscriptions/listen` updates and multi-round-trip destructive confirmations. Confirmation retries carry server-signed, single-use state tied to the exact command and shell context, expiring after 10 minutes. Clients using the `initialize` handshake retain session-based `elicitation/create`; their location subscriptions end when the session is deleted or has no open requests for 10 minutes. ([#231](https://github.com/Azure/CosmosDBShell/pull/231), [#232](https://github.com/Azure/CosmosDBShell/pull/232))
- The welcome screen, command-example descriptions, runtime errors, and theme-preview labels now use localization resources and the OS UI language when translations are available, with English fallback for untranslated text. Added and refreshed catalogs for Czech, German, Spanish, French, Italian, Japanese, Korean, Polish, Brazilian Portuguese, Russian, Turkish, and Simplified and Traditional Chinese. Executable examples, command names, and flags remain unchanged. ([#206](https://github.com/Azure/CosmosDBShell/pull/206), [#211](https://github.com/Azure/CosmosDBShell/pull/211), [#212](https://github.com/Azure/CosmosDBShell/pull/212), [#213](https://github.com/Azure/CosmosDBShell/pull/213), [#216](https://github.com/Azure/CosmosDBShell/pull/216), [#217](https://github.com/Azure/CosmosDBShell/pull/217))
- Cosmos DB SDK requests now include the shell application version in the user agent, making client versions identifiable in service diagnostics. ([#210](https://github.com/Azure/CosmosDBShell/pull/210))
- Destructive MCP confirmations now identify their target. The elicitation prompt adds the connected account endpoint and the current database/container location, and notes that explicit `--db`/`--con` arguments override that location. ([#207](https://github.com/Azure/CosmosDBShell/pull/207))
- Import and export no longer hold entire files in memory. CSV imports are parsed incrementally, and CSV exports spool documents to a private temporary file to determine the complete column set, so transfers no longer scale with document count. Allow temporary disk space for the CSV export spool in addition to the destination file. ([#207](https://github.com/Azure/CosmosDBShell/pull/207))
- Script diagnostics now report where a failure happened. Human-readable output shows the innermost source location first followed by the recorded function and script call sites, JSON errors carry the originating file, line, and column, and diagnostic logs retain both through the existing secret-redaction pipeline. Functions keep their defining file's location even when invoked from another file. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))
- The language server now applies the same validation as script execution: control-flow placement, duplicate function parameters, document-local function names, and commands and built-in options nested inside blocks, branches, loops, pipelines, and command expressions. Variable and function symbols are case-sensitive, so `$value` and `$Value` stay distinct. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))
- Host-requested cancellation now propagates through script files, blocks, loops, and function calls without being turned into a positional runtime error. The shell reports a neutral result, records the cancellation in the diagnostic log, and restores call scopes and source context. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))
- Documented the shell language in [programming](docs/programming.md): operator precedence and associativity, compound assignment, numeric promotion, a statement grammar, validation rules, and resource limits. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))
- `rm` accepts `--partition-key` (`--pk`) to restrict both the dry run and the deletion to one complete logical partition key, including typed and hierarchical keys. `rm <id> --key=id --partition-key=<pk>` uses a point read or point delete instead of scanning the container, and its dry run reports the item's `id`, `partitionKey`, and `etag`. The new `--etag` option deletes that item only if its ETag still matches; a mismatch fails without retrying. ([#230](https://github.com/Azure/CosmosDBShell/pull/230), [#229](https://github.com/Azure/CosmosDBShell/issues/229))

### Breaking changes

- MCP `query` and container-item `ls` no longer aggregate all pages in a single call. `max` bounds one page and must be positive; omitted or non-positive values use a default cap of 100. Clients must follow non-null `continuationToken` values rather than assuming a short page means exhaustion. A null token marks exhaustion unless `resultIncomplete` is true. ([#204](https://github.com/Azure/CosmosDBShell/pull/204))
- Malformed CSV files are now rejected instead of being silently misread. An unterminated or misplaced quote previously caused the remainder of the file to be absorbed into a single field, so the import reported success while writing corrupted items. Such files now abort with `Invalid CSV record at line <n>`. Imports that previously appeared to succeed may now fail and require the source file to be corrected. ([#207](https://github.com/Azure/CosmosDBShell/pull/207))
- Command text and script files are now fully parsed and validated before any of their statements run, so a syntax or semantic error prevents the entire input from executing rather than failing part-way through. Invalid control flow is rejected: `return` requires an enclosing function or script file, `break` and `continue` require an enclosing loop in the same function or script, and duplicate function parameter names are refused. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))
- Calling a function with too few or too many arguments is now a usage error that exits with code `2`, including calls inside expressions. The function body does not run. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))
- Integer arithmetic now reports overflow as an error instead of wrapping. Shell integer literal magnitudes must be between `0` and `2147483647`; because the minus sign is a separate unary operator, the minimum shell integer must be written as `-2147483647 - 1`. Standalone large integer literals inside JSON objects and arrays are preserved as JSON numbers instead of being rejected; this does not extend the shell's integer arithmetic range. ([#208](https://github.com/Azure/CosmosDBShell/pull/208), [#225](https://github.com/Azure/CosmosDBShell/pull/225))
- JSON construction now preserves decimal types, so `$object = {"value":3.0}` stores JSON `3.0` and `$object.value / 2` produces `1.5`. It previously stored `3` and performed integer division, producing `1`. Scripts that relied on the old truncation must be reviewed. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))
- A failed command expression now propagates its error instead of silently producing an empty result. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))
- Scripts are subject to fixed resource limits: a shared parser nesting budget of 128 entries, a maximum expression tree depth of 128 nodes, and at most 64 active function and script-file calls. Exceeding a limit fails with a diagnostic instead of continuing recursive parsing or execution. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))

### Fixes

- Explicit JSON `null` values in ordinary MCP arguments are treated as omitted rather than failing nullable option binding, and are not echoed into history. Required arguments still fail validation when absent. `continuation` and the `rm` safety options `partition-key`/`pk` and `etag` reject explicit nulls instead of silently removing paging or deletion safeguards. Numeric arguments in echoed commands, confirmations, and history now use invariant formatting so they replay correctly under comma-decimal locales. ([#227](https://github.com/Azure/CosmosDBShell/pull/227))
- Concurrent shell processes now merge saved history under a shared lock instead of overwriting each other's entries. Saves and clears publish a complete, flushed replacement, preserving the existing file on failed writes; failed clears report an error without discarding loaded history. Temporary history files use restricted permissions, including preserved access rules or owner-only access on Windows. ([#228](https://github.com/Azure/CosmosDBShell/pull/228))
- CSV export now preserves scalar, array, and null query results in a scalar column, including results mixed with objects. The header is empty by default and can be named with `COSMOSDB_SHELL_CSV_SCALAR_COLUMN`; a collision with an object property fails explicitly. CSV import ignores empty headers only for empty cells and reports a line/column error for non-empty values under an empty header. ([#224](https://github.com/Azure/CosmosDBShell/pull/224))
- Startup and interactive output no longer crash on terminals without ANSI support. Linux containers without ICU can run in .NET globalization-invariant mode using bundled English messages. ([#226](https://github.com/Azure/CosmosDBShell/pull/226))
- Numeric parsing and conversion now use invariant culture, so decimal values in scripts and JSON properties behave consistently under comma-decimal locales. Large integer literals in JSON objects and arrays retain their exact JSON representation rather than overflowing the shell's 32-bit integer parser. ([#225](https://github.com/Azure/CosmosDBShell/pull/225))
- Data-plane indexing-policy reads and replacements now preserve included/excluded paths, composite indexes, spatial indexes, and vector indexes instead of losing SDK collection properties during serialization. The policy conversion uses the Cosmos SDK's Newtonsoft.Json contract; document serialization remains on System.Text.Json. ([#215](https://github.com/Azure/CosmosDBShell/pull/215))
- `mkdb`, `mkcon`, `create database`, and `create container` now work on serverless accounts. They previously requested autoscale throughput even when `--scale` and `--ru` were omitted, which serverless accounts reject. Omitting both options now creates the resource without throughput settings; supplying either option on a serverless account fails with an explanation. Provisioned accounts keep the existing autoscale default of 1000 RU/s. ([#222](https://github.com/Azure/CosmosDBShell/pull/222), [#218](https://github.com/Azure/CosmosDBShell/issues/218))
- Vector `ORDER BY`, `ORDER BY RANK` relevance ranking, and object-shaped `DISTINCT` projections no longer fail with a continuation-token error. These query pipelines execute successfully but cannot export a resumable token, which was previously reported as a command failure. Such queries now return their documents; through MCP they keep reading until the requested limit instead of stopping after one page, and a truncated result is reported as `resultIncomplete` rather than as an exhausted result set. ([#221](https://github.com/Azure/CosmosDBShell/pull/221), [#219](https://github.com/Azure/CosmosDBShell/issues/219))
- Local emulator outages are now detected across Cosmos DB commands. Requests fail promptly with an error and return the shell to its disconnected state instead of leaving an unresponsive session labeled as connected. ([#200](https://github.com/Azure/CosmosDBShell/pull/200))
- A failed or cancelled export no longer destroys its destination file. Exports are written to a temporary file in the destination directory and moved into place only after they complete, so an existing file survives query failures, write failures, and cancellation. An abrupt process termination can leave an unfinished `.cosmos-export-*.tmp` file behind. ([#207](https://github.com/Azure/CosmosDBShell/pull/207))
- `export --max` no longer requests a further query page once the limit is reached, so the reported request charge no longer includes a page whose items were discarded. Query iterators are now disposed. ([#207](https://github.com/Azure/CosmosDBShell/pull/207))
- Shell and MCP command execution is serialized, including nested shell calls, so concurrent requests can no longer interleave and corrupt the shared connection and navigation state. Waiting for a destructive confirmation does not hold the execution lock. ([#207](https://github.com/Azure/CosmosDBShell/pull/207))
- A destructive MCP command is refused when the connection or navigation context changes while its confirmation is pending, including navigating away and back. It previously ran against the changed context. ([#207](https://github.com/Azure/CosmosDBShell/pull/207))
- Echoing an MCP command line no longer fails the command it announces on hosts without an ANSI terminal, which previously reported `Terminal does not support ANSI` instead of running it. ([#207](https://github.com/Azure/CosmosDBShell/pull/207))
- `export` now rejects a directory as its destination and rejects an existing file before running its query instead of after. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))
- `return` no longer leaves the previous statement's custom renderer and explicit output format active, which could display an earlier command's output in place of the returned value. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))
- Attaching a source location to a runtime failure no longer changes its exit-code category, so authentication, throttling, connectivity, and arithmetic failures are no longer reported as usage errors. Parser errors raised from a script file keep that file's name and source text, including when reached through a command expression. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))
- Loops and functions preserve JSON `null` values, and numeric conditions use the same zero/nonzero rule for shell values and JSON properties, including fractional numbers. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))
- The language server records a `do` loop's body before its condition and records a `for` binder as the loop variable's definition, so hover and go-to-definition no longer resolve to the wrong occurrence. ([#208](https://github.com/Azure/CosmosDBShell/pull/208))
- MCP command lines now list positional arguments in the order the command binds them. A destructive confirmation and the recorded history entry previously followed the client's argument order, so `rmdb` could display its `force` flag in place of the database name. A call that supplies a positional argument while omitting an earlier one is now rejected, because the shell cannot express that call and the recorded command would bind differently on replay. ([#207](https://github.com/Azure/CosmosDBShell/pull/207))
- MCP invocations are now saved to the history file as they run and are bounded by the history size limit. They were previously saved only when a later interactive command was entered. On Linux and macOS, the history file is now restricted to its owner, including an existing file that was previously readable by other users. ([#207](https://github.com/Azure/CosmosDBShell/pull/207))

### Build & pipeline

- Added a dependency on CsvHelper 33.1.0 for CSV parsing. ([#207](https://github.com/Azure/CosmosDBShell/pull/207))
- Upgraded ModelContextProtocol and ModelContextProtocol.AspNetCore to 2.2.0 for the updated HTTP transport and subscription lifecycle. ([#231](https://github.com/Azure/CosmosDBShell/pull/231), [#232](https://github.com/Azure/CosmosDBShell/pull/232))
- Added OneLoc catalog export, translation-resource generation, and CI checks that reject missing or stale committed localization catalogs. Local builds refresh the source catalog; CI verifies it without rewriting it. The localization tool project is now included in the solution. ([#206](https://github.com/Azure/CosmosDBShell/pull/206), [#220](https://github.com/Azure/CosmosDBShell/pull/220))
- Pinned GitHub Actions to full-length commit SHAs and updated the grouped Actions dependencies. ([#202](https://github.com/Azure/CosmosDBShell/pull/202), [#203](https://github.com/Azure/CosmosDBShell/pull/203))

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
- **Request charge in MCP structured results.** Instrumented data-plane commands (`query`, including `--explain`; `print`; container-scoped `ls`; `can-i` probes; `batch run`; `mkitem`; `replace`; `patch`; `rm`; `import`; and `export`) now report the Cosmos DB request charge (in RUs) consumed by the operation as a uniform `requestCharge` field on the MCP tool result, so agents can track observed RU cost consistently across calls. Budget enforcement remains tracked separately in #162. ([#162](https://github.com/Azure/CosmosDBShell/issues/162))
- **Connection-scoped request-charge totals.** The shell accumulates request charges observed from instrumented commands and reports the total in `info` as `session.requestCharge`, together with the number of positively charged command operations as `session.chargedOperationCount`. A successful `connect` starts new totals; database and container navigation do not reset them. This is usage telemetry, not budget enforcement or billing data. ([#162](https://github.com/Azure/CosmosDBShell/issues/162))
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
