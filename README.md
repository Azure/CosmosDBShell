# Azure Cosmos DB Shell

Lightweight CLI for Azure Cosmos DB.

## Features

- Connect via Entra ID, connection string, or Azure CLI/Developer tools
- Navigate with `ls` and `cd` (Account -> Databases -> Containers -> Items)
- Create, query, delete: `mkdb`, `mkcon`, `mkitem`, `query`, `rm`
- Pipelines and scripting with variables, loops, functions
- MCP server for AI/tool integration

## Quick Start

**Requirements:** .NET SDK 10.0+

```bash
dotnet run --project CosmosDBShell
```

**Example session:**

```text
connect "AccountEndpoint=...;AccountKey=..."
ls                          # list databases
cd MyDatabase
ls                          # list containers
cd MyContainer
query "SELECT * FROM c"
```

## Install from NuGet package artifacts

When consuming build artifacts (`*.nupkg`) from this repo, install as a .NET global tool.

`dotnet tool install` for these packages requires .NET 10 because the tool packages target `net10.0`.

1. Download the NuGet package(s) to a local folder.
2. Install from that folder with `--add-source`.

### Platform package IDs

- Linux x64: `CosmosDBShell.linux-x64`
- Linux ARM64: `CosmosDBShell.linux-arm64`
- macOS x64: `CosmosDBShell.osx-x64`
- macOS ARM64: `CosmosDBShell.osx-arm64`
- Windows x64: `CosmosDBShell.win-x64`

### Install commands

Linux x64:

```bash
dotnet tool install --global CosmosDBShell.linux-x64 --add-source /path/to/nupkgs --version <version>
```

Linux ARM64:

```bash
dotnet tool install --global CosmosDBShell.linux-arm64 --add-source /path/to/nupkgs --version <version>
```

macOS x64:

```bash
dotnet tool install --global CosmosDBShell.osx-x64 --add-source /path/to/nupkgs --version <version>
```

macOS ARM64:

```bash
dotnet tool install --global CosmosDBShell.osx-arm64 --add-source /path/to/nupkgs --version <version>
```

Windows x64 (PowerShell):

```powershell
dotnet tool install --global CosmosDBShell.win-x64 --add-source C:\path\to\nupkgs --version <version>
```

If your feed includes the base tool package (`CosmosDBShell.<version>.nupkg`) and its RID package, this also works:

```bash
dotnet tool install --global CosmosDBShell --add-source /path/to/nupkgs --version <version>
```

### Use, update, uninstall

Run the tool:

```bash
cosmosdbshell
```

Update:

```bash
dotnet tool update --global <package-id> --add-source /path/to/nupkgs --version <new-version>
```

Uninstall:

List the installed global tools first so you can identify the exact package ID:

```bash
dotnet tool list --global
```

Then uninstall the matching package ID. For example, if you installed the Windows x64 RID-specific package:

```powershell
dotnet tool uninstall --global CosmosDBShell.win-x64
```

If you installed the base package instead:

```bash
dotnet tool uninstall --global CosmosDBShell
```

## Documentation

- [Connection](docs/connect.md) - Authentication and connection options
- [Commands](docs/commands.md) - All shell commands
- [Navigation](docs/navigation.md) - Navigation, pipes, CLI arguments
- [Programming](docs/programming.md) - Variables, control flow, functions
- [MCP](docs/mcp.md) - Model Context Protocol integration

## CLI Arguments

| Option | Description |
| ------ | ----------- |
| `-c <cmd>` | Execute and exit |
| `-k <cmd>` | Execute and stay |
| `--connect <str>` | Connection string or endpoint URL |
| `--connect-tenant <id>` | Entra ID tenant for interactive login |
| `--connect-hint <email>` | Login hint for interactive login |
| `--connect-authority-host <uri>` | Authority host (e.g. sovereign clouds) |
| `--connect-managed-identity <id>` | Use a user-assigned managed identity |
| `--mcp [port]` | Enable MCP server on the given port, or `6128` by default |
| `--verbose` | Print full exception details |
| `--cs <n>` | Colors: 0=off, 1=standard, 2=truecolor |
| `--help` | Show help |

## CI And Packaging

This repo currently uses one GitHub Actions workflow for validation and package artifacts:

- [.github/workflows/validate-and-package.yml](.github/workflows/validate-and-package.yml): runs validation on pull requests, and on branch pushes or manual runs it also builds installable RID-specific NuGet tool packages and uploads them as workflow artifacts

GitHub Actions uses [.github/nuget.github.config](.github/nuget.github.config) so restores do not depend on the Azure DevOps feed.

Packaging runs produce preview versions in the form `1.0.<run>-preview.<branch>`, upload separate artifacts for each RID-specific package plus a pointer/base package artifact for the non-RID package ID, and write a workflow summary with artifact names plus ready-to-use `dotnet tool install` commands.

## License

Microsoft Corporation. All rights reserved.
