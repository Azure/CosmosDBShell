# GitHub Copilot Instructions

## Project Overview

- This repository is a C#/.NET CLI for Azure Cosmos DB shell workflows.
- The main app lives in `CosmosDBShell/`.
- Tests live in `CosmosDBShell.Tests/`.
- Docs live in `docs/` and the root `README.md`.

## Coding Guidelines

- Keep changes minimal and targeted. Do not refactor unrelated code while fixing a focused issue.
- Match the existing C# style and file structure used in the surrounding code.
- Prefer clear names over abbreviations unless the command surface already established the shorthand.
- Avoid adding comments unless they explain a non-obvious constraint or lifecycle detail.

## Commands

- Shell commands live in `CosmosDBShell/Azure.Data.Cosmos.Shell.Commands/`.
- New or changed commands should use the existing metadata attributes:
  - `CosmosCommand`
  - `CosmosExample`
  - `CosmosOption`
  - `CosmosParameter`
- When renaming a command, update all of the following together:
  - command attribute name
  - examples
  - help/localization strings
  - docs
  - tests
  - class and file names when appropriate

## Localization And Help Text

- User-facing command descriptions and option descriptions are stored in `CosmosDBShell/lang/en.ftl`.
- Keep help text aligned with the real CLI behavior.
- If a CLI option changes, update both the localized help strings and the user documentation.

## Documentation

- Update `README.md` for user-visible CLI changes.
- Always retain `## Unreleased` at the top of `CHANGELOG.md`, even when it is empty. When preparing a release, move its entries into a dated version section below it; never replace or remove the Unreleased heading.
- Update the relevant docs in `docs/`, especially:
  - `docs/commands.md` for command usage
  - `docs/navigation.md` for CLI arguments and shell navigation
  - `docs/mcp.md` for MCP behavior

## Tests And Validation

- Add or update tests in `CosmosDBShell.Tests/` when changing behavior.
- Prefer focused command tests for command behavior changes.
- Validate changes with build at minimum:
  - `dotnet build CosmosDBShell/CosmosDBShell.csproj`
  - `dotnet build CosmosDBShell.Tests/CosmosDBShell.Tests.csproj`

## Project-Specific Pitfalls

- Be careful with state transitions in the shell. Some states share the same `CosmosClient`, so disposing the old state during navigation can break the new state.
- Be careful with iterator lifetimes when returning `IAsyncEnumerable<T>`; do not dispose iterators before enumeration completes.
- MCP supports two transports. `--mcp [port]` starts the HTTP server (default port `6128`). `--mcp-stdio` runs a headless server where stdin/stdout carry only MCP JSON-RPC and diagnostics go to stderr; it cannot be combined with `--mcp`, `--lsp`, `--stdio`, `-c`, `-k`, or `--clear-history`.
- In MCP stdio mode, nothing may write to the protocol stdout. Route user-facing output through `ShellOutput` (enforced by analyzer rule CZ0003), and never read console input.

## Preferred Change Pattern

- Fix root causes instead of patching symptoms.
- Preserve public behavior unless the task explicitly changes the CLI or output contract.
- If behavior changes, update tests and docs in the same change.

## Commits

- When creating Git commits, do not add Copilot or AI `Co-authored-by` trailers.
- Ensure commit messages are clear, concise, and follow the project's existing style.
- Use the imperative mood in commit messages (e.g., "Add feature" instead of "Added feature").
- Reference relevant issues or pull requests in the commit message when applicable.
