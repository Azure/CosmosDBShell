# Contributing to Azure Cosmos DB Shell

We welcome contributions and suggestions! Whether it's a bug fix, new feature, documentation improvement, or just a question — we'd love to hear from you.

## Contributor License Agreement (CLA)

Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution. For details, visit the [CLA site](https://cla.microsoft.com).

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions provided by the bot. You will only need to do this once across all repositories using our CLA.

## Ways to Contribute

### Ideas, Feature Requests, and Bugs

We are open to all ideas and we want to get rid of bugs! Use the [Issues](https://github.com/Azure/CosmosDBShell/issues) section to:

- Report a bug
- Suggest a new feature or enhancement
- Ask a question or start a discussion

Look for issues labeled [`good first issue`](https://github.com/Azure/CosmosDBShell/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22) if you're looking for a place to start.

### Documentation

Found a typo or strangely worded sentences? Submit a PR! Documentation lives in the `docs/` folder and the root `README.md`.

### Code

Contribute bug fixes, features, or design changes by following the development setup below.

## Development Setup

### Prerequisites

- [.NET SDK 10.0+](https://dotnet.microsoft.com/download) (verify with `dotnet --list-sdks`)
- [Git](https://git-scm.com/)
- A code editor (VS Code recommended — the repo includes a build task)

### Getting Started

```bash
# Clone the repository
git clone https://github.com/Azure/CosmosDBShell.git
cd CosmosDBShell

# Restore dependencies
dotnet restore CosmosDBShell.sln

# Build the solution
dotnet build CosmosDBShell.sln

# Run the shell locally
dotnet run --project CosmosDBShell/CosmosDBShell.csproj

# Run tests
dotnet test CosmosDBShell.sln
```

In VS Code, you can also build with **Ctrl+Shift+B** (Windows/Linux) or **Cmd+Shift+B** (macOS) (uses the predefined build task).

### Project Architecture

```
CosmosDBShell/
├── Azure.Data.Cosmos.Shell.Commands/   # Each shell command (ls, cd, query, etc.)
├── Azure.Data.Cosmos.Shell.Core/       # Interpreter, state machine, command runner
├── Azure.Data.Cosmos.Shell.Parser/     # Lexer and AST for shell syntax
├── Azure.Data.Cosmos.Shell.States/     # Shell state (connected, in database, etc.)
├── Azure.Data.Cosmos.Shell.Mcp/        # MCP (Model Context Protocol) server
├── Azure.Data.Cosmos.Shell.Lsp/        # LSP server for editor integration
├── Azure.Data.Cosmos.Shell.Util/       # Shared utilities and helpers
├── Azure.Data.Cosmos.Shell.KeyBindings/ # Key binding definitions
├── lang/                               # Localization files (Fluent .ftl)
└── Program.cs                          # Entry point and CLI option parsing

CosmosDBShell.Tests/                    # Unit and integration tests
docs/                                   # User-facing documentation
```

### Key Conventions

- **Commands** are classes in `Azure.Data.Cosmos.Shell.Commands/` using `[CosmosCommand]`, `[CosmosExample]`, `[CosmosOption]`, and `[CosmosParameter]` attributes.
- **Localization** strings live in `lang/en.ftl` (Fluent format). Keep help text aligned with actual CLI behavior.
- **Tests** live in `CosmosDBShell.Tests/`. Add or update tests when changing behavior.
- Match the existing C# style. Prefer clear names over abbreviations.

### Running Against the Emulator

You can develop and test without an Azure subscription by using the [Azure Cosmos DB Emulator](https://learn.microsoft.com/azure/cosmos-db/emulator):

```bash
dotnet run --project CosmosDBShell/CosmosDBShell.csproj -- --connect "https://localhost:8081"
```

## Submitting a Pull Request

1. Fork the repository and create a feature branch from `main`.
2. Make your changes — keep them focused and minimal.
3. Add or update tests for any behavior changes.
4. Update documentation (`README.md`, `docs/`) if the change is user-visible.
5. Ensure the build passes: `dotnet build CosmosDBShell.sln`
6. Ensure tests pass: `dotnet test CosmosDBShell.sln`
7. Open a pull request against `main` with a clear description of what and why.

### CI Pipelines

- **GitHub Actions** ([.github/workflows/validate-and-package.yml](.github/workflows/validate-and-package.yml)) runs validation on pull requests, and on branch pushes also builds NuGet tool packages.
- **Azure Pipelines** ([.pipelines/CosmosDB-Shell-Official.yml](.pipelines/CosmosDB-Shell-Official.yml)) handles signed builds and publishing from `main`.

Local builds use the default NuGet sources (nuget.org).

## Code of Conduct

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
