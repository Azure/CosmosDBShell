# Portable CLI contract tests

The [shared suite](../tests/cli-contracts) exercises an executable, not C# objects.
It is the compatibility contract for a future Rust shell. The same case IDs,
scripts, fixtures, exit codes, and expectations apply to every implementation.
No implementation switches, reduced expectations, automatic baseline updates,
`xfail`, or fallback to the C# binary are supported.

The Rust port itself is not included. Passing the C# reference does not establish
Rust compatibility. A Rust implementation must pass every offline case and both
emulator profiles before it is considered compatible.

## Run locally

Use Python 3.11 or newer. The runner itself does not require .NET; building the
C# reference does. From the repository root, in PowerShell:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -r tests\cli-contracts\requirements.txt
dotnet build CosmosDBShell\CosmosDBShell.csproj
$assembly = & .\tests\cli-contracts\resolve-shell-assembly.ps1
python -m pytest tests\cli-contracts --profile offline --shell-executable dotnet --shell-prefix-arg $assembly -q
```

On Linux/macOS, activate `.venv/bin/activate` and use `/` in paths.
For a published or Rust executable, substitute only the launcher:

```powershell
python -m pytest tests\cli-contracts --profile offline --shell-executable C:\path\to\cosmos-shell.exe -q
```

`--shell-prefix-arg` is repeatable (use `--shell-prefix-arg=-flag` for a prefix
argument beginning with `-`). Launchers are never evaluated by an OS shell.
Relative executable/assembly paths are resolved before changing directories.
The runner requests English application messages with
`COSMOSDB_SHELL_UI_LANGUAGE=en`, independently of the parent process's language.
The English catalog is also available with .NET invariant globalization enabled.

Run just the runner/comparator self-tests without building a shell:

```powershell
python -m pytest tests\cli-contracts\test_runner.py tests\cli-contracts\test_comparisons.py -q
```

For emulator contracts, install the independent fixture dependencies and start
the appropriate local emulator:

```powershell
python -m pip install -r tests\cli-contracts\requirements-emulator.txt
python -m pytest tests\cli-contracts\test_contracts.py --profile emulator-windows --shell-executable C:\path\to\cosmos-shell.exe -q
```

Use `--profile emulator-linux` for the Linux preview emulator. These are server
profiles, not implementation capabilities. The Windows profile also checks
partition-key collisions, Windows emulator policy defaults and offer metadata,
bounded change feed results, and stored procedure, trigger, and UDF registration
lifecycles (not script execution). Missing executables,
an unavailable selected emulator, malformed cases, and missing output files
**fail**; they are not skipped. `--emulator-endpoint` defaults to
`https://localhost:8081` and permits only `localhost` and `127.0.0.1`.

The command smoke cases exercise real invocations, not just option parsing.
`edit` is covered by its noninteractive diagnostic and must not create a file;
opening an editor interactively requires a terminal harness. `jq` is outside
the current compatibility contract.

The Python Azure Cosmos SDK creates fresh, uniquely named, owned databases,
seeds data independently of the shell, verifies mutations, and cleans up on
success or failure. Never point this suite at a real account or add real
credentials or production data to a case.

## Capturing only command output

Every case runs a fresh process in an isolated working/config directory with
closed stdin and a timeout. Related commands share a script/session. The runner
captures subprocess stdout and stderr into `stdout.txt` and `stderr.txt`.

Structured contracts normally redirect each command's result inside the shell:

```text
print {"id":"one","count":9007199254740993} > 'result.json'
```

Only declared output files are compared. An interactive prompt cannot contaminate
`result.json`; the runner never guesses or removes prompt text. A prompt, ANSI
escape, or duplicate JSON result *inside* the declared file causes a failure.

Internal redirection bypasses the human renderer. Human/table cases therefore
use `--output user`/`--output table` without internal result redirection and
assert the captured subprocess files instead. The `-c` invocation exits without
entering the interactive editor. Startup/argument errors also use subprocess
files and assert the process exit code.

## Case format

[schema.json](../tests/cli-contracts/schema.json) validates every manifest before
selection. Files contain `schemaVersion: 1` and a `cases` array. IDs are unique
across the entire corpus. Source links reference existing xUnit method names or
explicitly reviewed removed methods in the migration map.

```json
{
  "schemaVersion": 1,
  "cases": [{
    "id": "example.structured-result",
    "profiles": ["offline"],
    "sources": ["CosmosDBShell.Tests/Integration/OutputRedirectionTests.cs::Redirect_StdoutToFile_WritesOutput"],
    "script": "print {\"id\":\"one\"} > 'result.json'",
    "exitCode": 0,
    "files": {
      "result.json": {"mode": "json", "expected": {"id": "one"}},
      "stderr.txt": {"mode": "empty"}
    }
  }]
}
```

The example illustrates the format; use the actual method name from the source
test when adding a case. Use either `script` or raw CLI `args`; scripts get
`--output <format> -c <script>` (`json` by default), whereas `args` are passed
verbatim. `fixtures` maps relative filenames to UTF-8 input text.
`timeoutSeconds` defaults to 45 and is capped at 120.

Placeholders use only `${ALL_CAPS}`. `${WORK}` is the native case directory;
`${WORK_SHELL}` uses forward slashes and doubles apostrophes for a shell
single-quoted path. Emulator fixtures supply names such as `${DB}` and
`${ENDPOINT}`. Unknown placeholders are errors. JSON braces and normal shell
variables do not need escaping. Output/fixture paths must remain inside the
case directory.

### Comparisons

- `json`: exact data, types and keys; ignores whitespace and object key order.
  Boolean/string/null/absent values remain distinct. Numbers are parsed as exact
  integers/decimals, not binary floats. Arrays are ordered unless their JSON
  pointers are explicitly listed in `unordered`; multiplicity is preserved.
- `jsonl`: one JSON value per nonempty line, with the same strict comparisons.
- `csv`: parsed rows of strings, including the header in `expected`. Column and
  row order, spaces, empty cells, embedded newlines and Unicode matter.
  `delimiter` defaults to comma. Equivalent CSV quoting is accepted.
- `text`: exact `expected` text, or semantic `contains`, `notContains`,
  `lineCounts` and substring `occurrences` assertions. No universal whitespace/ANSI/prompt normalization.
  Assert labels, values and counts, not table borders or terminal widths.
  Include counts on stable labels/values to reject duplicated rendered output.
- `bytes`, `empty`, `absent`: exact byte arrays, an existing empty file, or
  no file, respectively.

JSON specifications may additionally include a standard, self-contained JSON
`schema` (remote references are forbidden). Schema-only comparisons preserve
existing shape tests; prefer full expected data for deterministic results.

Dynamic fields are not silently deleted. List exact JSON pointers in `dynamic`,
validate each actual value with a JSON Schema, and include a placeholder at the
same path in `expected`:

```json
{
  "mode": "json",
  "expected": {"id": "one", "requestCharge": null},
  "dynamic": {
    "/requestCharge": {"type": "number", "exclusiveMinimum": 0}
  }
}
```

The field must exist and satisfy the rule. Everything else still matches
exactly. Test token reuse, ETag relationships, and side effects in a stateful
scenario, rather than merely ignoring those fields.

## Migration and maintenance

### C# code coverage

The Validate And Package workflow runs the offline Python suite under
`dotnet-coverage`. Its profiler follows the shell child processes, so the report
measures C# product code exercised by the contracts, not Python code. The runner
and contract expectations are unchanged. CI fails if no shell product lines are
covered. ReportGenerator merges the reports for the GitHub Code Quality upload
only after both runs succeed and record shell product hits. Raw reports remain
available as diagnostic artifacts on failure. Both runs use the same collector and product
module filter (shell, localization and analyzer). This avoids counting generated
async classes twice due to different class names from different collectors.
Test assemblies and dependencies are excluded. Emulator contracts are not
included in this coverage run.

To collect the same coverage locally after building the C# reference:

```powershell
dotnet tool restore
$assembly = & .\tests\cli-contracts\resolve-shell-assembly.ps1
dotnet tool run dotnet-coverage collect --settings tests\cli-contracts\coverage.config --output TestResults\cli-contracts\coverage.cobertura.xml --output-format cobertura python -m pytest tests\cli-contracts --profile offline --shell-executable dotnet --shell-prefix-arg $assembly -q
```

This instrumentation is specific to the C# reference. A future Rust executable
will need its own coverage collector while using the same portable contracts.

### Tracking migration

The [migration map](../tests/cli-contracts/migration-map.json) and
[inventory tool](../tests/cli-contracts/inventory.py) track source methods,
portable coverage links, remaining implementation assertions and environment
requirements:

```powershell
python tests\cli-contracts\inventory.py --output artifacts\cli-contract-inventory.json
```

A source link means related external behavior is covered, **not** that every
assertion/theory row has been migrated. The inventory must report remaining
coverage honestly. Keep xUnit tests until all their relevant assertions have
equivalent external coverage. AST types, internal scope ownership, iterator/client
lifetimes, request counts, races, MCP/LSP wire protocols and interactive editor
behavior remain implementation-specific.

Once every observable assertion and theory row has equivalent passing contracts,
remove the redundant xUnit method and record its original `source`, exact
replacement `cases`, and review `reason` in `migratedMethods` in the migration map.
The inventory rejects entries for still-existing methods, unknown removed sources,
missing replacement cases and differences between reviewed and actual case links.
Migration history is reported separately from active methods; a partial source
link never becomes a completed migration automatically.

`FilterCommandTests` is the first migrated block: 20 methods (24 executed test
rows) have been replaced by portable contracts. Five methods in C# check
state/result identity, normalization to `ShellJson` before rendering, renderer
ownership, cancellation and exception wrapping. The seven output-only methods
from `FilterIntegrationTests` and `EchoTests` now run as portable contracts.

The four `OutputRedirectionTests` and three output-only `ScriptArgumentTests`
methods have also migrated. Contracts distinguish human-readable and JSON error
redirection, verify the actual script path and argument values (including spaces),
and prove that a successfully executed child script does not leak its variable.
The remaining script control-flow method keeps its internal result-type and
function-registry assertions in C#; its numeric return value is also checked by a
strict JSON contract.

Version/help output and filter builtin/path values have also migrated. Both
shortcut-help variants check the complete ordered shortcut list, nonblank metadata
and search-direction descriptions. Filter cases exercise scalar inputs directly,
mixed-kind and array-key sorting, nested-array equality and every overflow-index
variant. C# keeps consolidated theories for the original internal result and
exception types, plus the original decimal-representation, sequence and parser
error-position checks. These are behavior migrations, not removal of all
implementation-specific coverage.

Add reviewed expectations alongside each behavior change. Never generate
expected files from whatever the current implementation happens to print and
accept them automatically. Never relax a contract because one implementation
does not support it yet.

CI runs the offline suite on Windows/Linux and emulator cases against both
emulator engines, in addition to the existing C# checks. Future Rust CI must
invoke these exact profiles with its artifact, without building C#. To save
JUnit and failure artifacts locally:

```powershell
python -m pytest tests\cli-contracts --profile offline --shell-executable C:\path\to\cosmos-shell.exe --junitxml artifacts\contracts.xml --contract-artifacts artifacts\contract-failures -q
```

Failure folders contain only declared synthetic fixtures, the case definition,
and isolated process output; inherited environment variables are not recorded.
Use a new artifacts directory for each run: previous failure evidence is never
silently overwritten. Upload only this dedicated synthetic test data, never
arbitrary shell sessions, real-account diagnostics, or local configuration.
