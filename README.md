# Azure Cosmos DB Shell

Lightweight CLI for Azure Cosmos DB.

## Features

- Connect via Entra ID, connection string, or Azure CLI/Developer tools
- Navigate with `ls` and `cd` (Account -> Databases -> Containers -> Items)
- Inspect the current location with `pwd`
- Create, query, delete: `mkdb`, `mkcon`, `mkitem`, `query`, `rm`
- Pipelines and scripting with variables, loops, functions
- MCP server for AI/tool integration

## Quick Start

**Requirements:** .NET SDK 9.0+

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

```bash
dotnet tool uninstall --global <package-id>
```

## Documentation

- [Connection](docs/connect.md) - Authentication and connection options
- [Commands](docs/commands.md) - All shell commands
- [Navigation](docs/navigation.md) - Navigation, pipes, CLI arguments
- [Programming](docs/programming.md) - Variables, control flow, functions
- [MCP](docs/mcp.md) - Model Context Protocol integration

## CI And Packaging

GitHub Actions handles PR validation and unsigned package creation:

- [.github/workflows/ci.yml](.github/workflows/ci.yml): restore, build, test, and fuzzer smoke test (runs on every PR)
- [.github/workflows/package-unsigned.yml](.github/workflows/package-unsigned.yml): build and upload unsigned NuGet artifacts (runs on tags)

GitHub Actions uses [.github/nuget.github.config](.github/nuget.github.config) so the workflows restore packages from nuget.org without depending on the Azure DevOps feed.

Azure Pipelines ([.pipelines/CosmosDB-Shell-Official.yml](.pipelines/CosmosDB-Shell-Official.yml)) handles signing and publishing via the internal Azure setup.

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

## License

Microsoft Corporation. All rights reserved.
