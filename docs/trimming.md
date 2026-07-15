# Trimming

Published builds are trimmed by default to reduce the self-contained distribution size. Trimming applies to every `dotnet publish` and `dotnet pack` of the application, including the release pipeline and NuGet tool packages.

Reflection-based `System.Text.Json` serialization is intentionally kept enabled, so dynamic command and result types continue to serialize without source-generated metadata.

## Compare trimmed and untrimmed output

From the repository root on Windows:

```powershell
./tools/measure-trimming.ps1
```

Choose another runtime identifier or increase the startup sample count:

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