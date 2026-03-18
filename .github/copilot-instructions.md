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
- MCP in this repo is HTTP-only. The `--mcp` option optionally accepts a port and defaults to `6128`.

## Preferred Change Pattern

- Fix root causes instead of patching symptoms.
- Preserve public behavior unless the task explicitly changes the CLI or output contract.
- If behavior changes, update tests and docs in the same change.
