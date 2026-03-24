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

## Documentation

- [Commands](docs/commands.md) - All shell commands
- [Navigation](docs/navigation.md) - Navigation, pipes, CLI arguments
- [Programming](docs/programming.md) - Variables, control flow, functions
- [MCP](docs/mcp.md) - Model Context Protocol integration

## CI And Packaging

GitHub Actions uses a single workflow for validation and branch/main package artifacts:

- [.github/workflows/validate-and-package.yml](.github/workflows/validate-and-package.yml): runs PR validation and uploads installable NuGet tool packages as artifacts on branch and main pushes

GitHub Actions uses [.github/nuget.github.config](.github/nuget.github.config) so the workflow restores packages from nuget.org without depending on the Azure DevOps feed.

The packaging job produces preview versions in the form `1.0.<run>-preview.<branch>`, uploads separate artifacts for the pointer package and each RID-specific package, and writes a summary with the artifact names plus ready-to-use `dotnet tool install` commands.

- [.github/workflows/validate-and-package.yml](.github/workflows/validate-and-package.yml): runs PR validation on pull requests and main branch pushes, and uploads installable NuGet tool packages as artifacts on branch and main pushes
- [.github/workflows/package-nuget-artifacts.yml](.github/workflows/package-nuget-artifacts.yml): build installable preview/unsigned NuGet tool packages for branch pushes and tags, and upload the `.nupkg` files as workflow artifacts

GitHub Actions uses [.github/nuget.github.config](.github/nuget.github.config) so the workflows restore packages from nuget.org without depending on the Azure DevOps feed.

The packaging workflow produces preview versions in the form `1.0.<run>-preview.<branch>`. Each workflow run also writes a summary with the exact artifact name and ready-to-use `dotnet tool install` commands so the package version is easy to find later.
| `--cs <n>` | Colors: 0=off, 1=standard, 2=truecolor |
| `--help` | Show help |

## License

Microsoft Corporation. All rights reserved.
