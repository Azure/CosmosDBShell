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

1. Download the base tool package (`Microsoft.CosmosDBShell.<version>.nupkg`) and the package for your runtime to the same local folder.
2. Install from that folder with `--add-source` using the base package ID `Microsoft.CosmosDBShell`.

### Runtime-specific package files

- Linux x64: `Microsoft.CosmosDBShell.linux-x64.<version>.nupkg`
- Linux ARM64: `Microsoft.CosmosDBShell.linux-arm64.<version>.nupkg`
- macOS x64: `Microsoft.CosmosDBShell.osx-x64.<version>.nupkg`
- macOS ARM64: `Microsoft.CosmosDBShell.osx-arm64.<version>.nupkg`
- Windows x64: `Microsoft.CosmosDBShell.win-x64.<version>.nupkg`

### Install command

After placing the base package and the matching runtime package in the same folder, install with the base package ID:

```bash
dotnet tool install --global Microsoft.CosmosDBShell --add-source /path/to/nupkgs --version <version>
```

Windows PowerShell example:

```powershell
dotnet tool install --global Microsoft.CosmosDBShell --add-source C:\path\to\nupkgs --version <version>
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

Then uninstall the tool by its package ID:

```bash
dotnet tool uninstall --global Microsoft.CosmosDBShell
```

## Documentation

- [Connection](docs/connect.md) - Authentication and connection options
- [Commands](docs/commands.md) - All shell commands
- [Navigation](docs/navigation.md) - Navigation, pipes, CLI arguments
- [Programming](docs/programming.md) - Variables, control flow, functions
- [MCP](docs/mcp.md) - Model Context Protocol integration

## CI And Packaging

This repo currently uses one GitHub Actions workflow for validation and package artifacts:

- [.github/workflows/validate-and-package.yml](.github/workflows/validate-and-package.yml): runs validation on pull requests, and on branch pushes or manual runs it also builds installable RID-specific NuGet tool packages and uploads them as workflow artifacts

Local builds and GitHub Actions use the default NuGet sources (nuget.org). The Azure DevOps pipeline uses a restricted config at [.pipelines/nuget.config](.pipelines/nuget.config) that limits restores to the internal feed.

Packaging runs produce preview versions in the form `1.0.<run>-preview.<branch>`, upload separate artifacts for each RID-specific package plus a pointer/base package artifact for the non-RID package ID, and the Azure pipeline publishes both the base package and the RID-specific packages to the internal feed.

## Command-Line Arguments

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

## How to Contribute

This project welcomes contributions and suggestions. To contribute, see these documents:

- [Code of Conduct](./CODE_OF_CONDUCT.md)
- [Security](./SECURITY.md)
- [Contributing](./CONTRIBUTING.md)

## License

[MIT](LICENSE.md)
