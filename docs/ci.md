# CI/CD guide

This guide covers installing and using the Azure Cosmos DB Shell
(`cosmosdbshell`) in continuous integration workflows.

## Install with the setup action

The [`setup-cosmosdb-shell`](../.github/actions/setup-cosmosdb-shell/README.md)
composite action downloads a self-contained build from the GitHub Releases,
caches it in the runner tool cache, and adds `cosmosdbshell` to `PATH`. No .NET
SDK is required on the runner.

```yaml
name: cosmos-ci

on:
  push:
    branches: [main]

jobs:
  seed:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7

      - name: Setup Azure Cosmos DB Shell
        uses: Azure/CosmosDBShell/.github/actions/setup-cosmosdb-shell@main
        with:
          version: latest # or pin a tag, e.g. v1.1.115

      - name: Verify
        run: cosmosdbshell --version

      - name: Seed a container
        env:
          COSMOS_CONNECTION: ${{ secrets.COSMOS_CONNECTION }}
        run: |
          cosmosdbshell -c "seed.csh \"$COSMOS_CONNECTION\" mydb mycontainer"
```

### Version pinning

Pin a specific release for reproducible builds:

```yaml
      - uses: Azure/CosmosDBShell/.github/actions/setup-cosmosdb-shell@main
        with:
          version: v1.1.115
```

Use `latest` to always track the newest release.

### Cross-platform matrix

The action supports Linux, macOS, and Windows runners on `x64` and `arm64`:

```yaml
jobs:
  test:
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: Azure/CosmosDBShell/.github/actions/setup-cosmosdb-shell@main
      - run: cosmosdbshell --version
```

## Alternative: install as a .NET global tool

If the runner already has the .NET SDK 10.0+ installed, you can install the
shell from the release NuGet packages instead. Download the pointer package
(`CosmosDBShell.<version>.nupkg`) and the runtime-specific package for your
runner into the same folder, then:

```bash
dotnet tool install --global CosmosDBShell --add-source ./nupkgs --version <version>
```

The setup action is preferred for CI because it does not require the .NET SDK
and caches the binary across runs.
