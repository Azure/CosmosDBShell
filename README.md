# Azure Cosmos DB Shell

Lightweight CLI for Azure Cosmos DB.

## Features

- Connect via Azure AD or connection string
- Navigate with `ls` and `cd` (Account -> Databases -> Containers -> Items)
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

If you are not sure which package ID is installed, list global tools first:

```bash
dotnet tool list --global
```

## Documentation

- [Commands](docs/commands.md) - All shell commands
- [Navigation](docs/navigation.md) - Navigation, pipes, CLI arguments
- [Programming](docs/programming.md) - Variables, control flow, functions
- [MCP](docs/mcp.md) - Model Context Protocol integration

## CI And Packaging

GitHub Actions handles PR validation and unsigned package creation:

- [.github/workflows/ci.yml](.github/workflows/ci.yml): restore, build, test, and fuzzer smoke test (runs on every PR)
- [.github/workflows/package-branches.yml](.github/workflows/package-branches.yml): build installable preview NuGet tool packages for every non-`main` branch push and upload the `.nupkg` files as workflow artifacts
- [.github/workflows/package-unsigned.yml](.github/workflows/package-unsigned.yml): build and upload unsigned NuGet artifacts (runs on tags)

GitHub Actions uses [.github/nuget.github.config](.github/nuget.github.config) so the workflows restore packages from nuget.org without depending on the Azure DevOps feed.

The branch packaging workflow produces preview versions in the form `1.0.<run>-preview.<branch>`. Each workflow run also writes a summary with the exact artifact name and ready-to-use `dotnet tool install` commands so the package version is easy to find later.

Azure Pipelines ([.pipelines/CosmosDB-Shell-Official.yml](.pipelines/CosmosDB-Shell-Official.yml)) handles signing and publishing via the internal Azure setup.

## CLI Arguments

| Option | Description |
| ------ | ----------- |
| `-c <cmd>` | Execute and exit |
| `-k <cmd>` | Execute and stay |
| `--connect <str>` | Initial connection |
| `--mcp [port]` | Enable MCP server on the given port, or `6128` by default |
| `--cs <n>` | Colors: 0=off, 1=standard, 2=truecolor |
| `--help` | Show help |

## License

Microsoft Corporation. All rights reserved.
