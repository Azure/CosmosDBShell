# Trimming

Published builds are trimmed by default to reduce the self-contained distribution size. Trimming is the default for `dotnet publish` and `dotnet pack` of the application, including the release pipeline and NuGet tool packages. Individual publishes can opt out with `-p:PublishTrimmed=false`; for example, the framework-dependent artifacts in the official pipeline are published untrimmed because trimming requires a self-contained publish.

Reflection-based `System.Text.Json` serialization is intentionally kept enabled, so dynamic command and result types continue to serialize without source-generated metadata.

## Compare trimmed and untrimmed output

Run the script from the repository root. Because it runs process-level smoke tests against the published binary, run it on an operating system that can execute the chosen `RuntimeIdentifier` (the default `win-x64` requires Windows):

```powershell
./tools/measure-trimming.ps1
```

Choose another runtime identifier or increase the startup sample count. The selected `RuntimeIdentifier` must be executable on the current OS, otherwise the smoke tests fail; for example, run the `linux-x64` measurement on Linux:

```powershell
./tools/measure-trimming.ps1 -RuntimeIdentifier linux-x64 -Iterations 5
```

The script publishes an untrimmed baseline and the default trimmed build, writes ZIP archives and Markdown/JSON summaries under `artifacts/trimming/`, and checks that both binaries:

- return success for `--version`;
- execute the shell `version` command successfully; and
- return failure for an invalid command.

## Diagnostic untrimmed build

Publishing is trimmed unless you opt out. To produce an untrimmed build for diagnostics:

```powershell
dotnet publish ./CosmosDBShell/CosmosDBShell.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=false
```

## Current compatibility constraint

Trimming uses partial mode and keeps reflection-based `System.Text.Json` serialization enabled. Disabling reflection-based serialization currently causes command execution to fail because command results and other dynamic shell types do not yet have complete source-generated JSON metadata.

The smoke tests are intentionally small. Before relying on a trimmed package, exercise parity for authentication, Azure Resource Manager operations, Cosmos DB data-plane operations, import/export, MCP, LSP, OpenTelemetry, and emulator workflows.

## Process tests against the trimmed executable

The `ShellProcessTests` integration tests launch the shell as a child process. By default they run `dotnet CosmosDBShell.dll` (the framework-dependent build). Set `COSMOSDBSHELL_PROCESS_TEST_EXE` to a published executable to run the same tests directly against a self-contained (for example trimmed) build:

```powershell
dotnet publish ./CosmosDBShell/CosmosDBShell.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:PublishTrimmed=true -p:TrimMode=partial `
  -o ./artifacts/trimmed-ci

$env:COSMOSDBSHELL_PROCESS_TEST_EXE = (Resolve-Path ./artifacts/trimmed-ci/CosmosDBShell.exe).Path
dotnet test ./CosmosDBShell.Tests/CosmosDBShell.Tests.csproj `
  --filter "FullyQualifiedName~ShellProcessTests&Category!=Emulator"
```

The `Validate And Package` CI workflow does this automatically: after the normal test run it publishes a trimmed `win-x64` executable and re-runs the process tests against it, so partial-trimming regressions fail the build.