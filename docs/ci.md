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
      - uses: actions/checkout@v4

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
      - uses: Azure/CosmosDBShell/.github/actions/setup-cosmosdb-shell@v1.1.115
        with:
          version: v1.1.115
```

For full reproducibility, pin both references: the action ref (`@v1.1.115` or a
commit SHA — not the moving `@main` branch) and the installed shell `version`.
Use `version: latest` only when you intentionally want to track the newest
release.

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

## Exit-code contract

Every non-interactive invocation (`-c`, `-k`, or a script piped on stdin) sets a
stable, machine-readable process exit code. Scripts can branch on these values.

| Code | Meaning | Typical causes |
| ---- | ------- | -------------- |
| `0` | Success | The command or script completed without error. |
| `1` | General failure | An error that does not fit a more specific category (for example a command that reported a failure, or a `4xx`/`5xx` that is not authentication or connectivity). |
| `2` | Authentication failure | Invalid, missing, or expired credentials; `401 Unauthorized`; `403 Forbidden`; a failed Entra credential. |
| `3` | Connection/network error | DNS/socket failure, `503 Service Unavailable`, or a request timeout (`408`/`504`). |
| `4` | Syntax error | The shell could not parse the command line or script (unbalanced quotes, unexpected end of input, an unknown option format, or an unrecognized CLI argument). |

These values form a public contract: existing codes are never repurposed, and
new failure categories get new codes. The interactive REPL always exits `0`.

```bash
cosmosdbshell -c "query \"SELECT * FROM c\""
case $? in
  0) echo "ok" ;;
  2) echo "check credentials"; exit 1 ;;
  3) echo "transient connectivity issue"; exit 1 ;;
  *) echo "command failed"; exit 1 ;;
esac
```

## Authentication patterns for CI

The shell selects a credential from the `connect` arguments (see the
[Connection Guide](connect.md) for the full decision tree). The patterns below
are the ones that fit CI runners.

### Workload identity federation (OIDC)

`azure/login` with OIDC sets the ambient environment variables that
`DefaultAzureCredential` reads, so connecting with the endpoint only (no
account key or token) authenticates as the federated identity. Only the
endpoint is required; `--connect-tenant`, `--connect-subscription`, and
`--connect-resource-group` are optional and used to make startup deterministic:

```yaml
permissions:
  id-token: write
  contents: read

jobs:
  seed:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

      - name: Setup Azure Cosmos DB Shell
        uses: Azure/CosmosDBShell/.github/actions/setup-cosmosdb-shell@main

      - name: Seed and validate test data
        run: |
          cosmosdbshell --connect https://myaccount.documents.azure.com:443/ \
            --connect-tenant ${{ secrets.AZURE_TENANT_ID }} \
            --connect-subscription ${{ secrets.AZURE_SUBSCRIPTION_ID }} \
            --connect-resource-group my-rg \
            -c "seed.csh"
```

The extra `--connect-*` options are optional: supplying `--connect-tenant`,
`--connect-subscription`, and `--connect-resource-group` skips ARM account
discovery for a deterministic startup in multi-subscription tenants.

### Managed identity (self-hosted runners)

On a self-hosted runner with an assigned managed identity, connect with
`--connect-managed-identity` (pass a client id for a user-assigned identity):

```bash
cosmosdbshell --connect https://myaccount.documents.azure.com:443/ \
  --connect-managed-identity <client-id> -c "seed.csh"
```

### Account key or static token (secrets)

For key-based accounts or a pre-fetched AAD token, pass the value through a
secret-backed environment variable rather than the command line:

```bash
# Account key
export COSMOSDB_SHELL_ACCOUNT_KEY="${{ secrets.COSMOS_KEY }}"
cosmosdbshell --connect https://myaccount.documents.azure.com:443/ -c "seed.csh"

# Static AAD access token
export COSMOSDB_SHELL_TOKEN="${{ secrets.COSMOS_TOKEN }}"
cosmosdbshell --connect https://myaccount.documents.azure.com:443/ -c "seed.csh"
```

## Failure-handling guidance

### Retry transient failures

Exit code `3` (and sometimes `1`) indicates a transient condition worth
retrying with backoff:

```bash
for attempt in 1 2 3; do
  cosmosdbshell -c "query \"SELECT * FROM c\"" && break
  code=$?
  # Only retry connectivity errors; fail fast on auth or syntax errors.
  if [ "$code" -ne 3 ]; then exit "$code"; fi
  sleep $((attempt * 5))
done
```

### Parse JSON output in scripts

Request JSON output and pipe it to a JSON processor. Set the format with the
`COSMOSDB_SHELL_FORMAT` environment variable or the shell's own `filter`
command:

```bash
export COSMOSDB_SHELL_FORMAT=json
count=$(cosmosdbshell -c "query \"SELECT VALUE COUNT(1) FROM c\"" | jq '.[0]')
echo "document count: $count"
```

### azd post-provision hooks

Run seed or validation scripts after `azd provision` by wiring the shell into a
hook in `azure.yaml`. The hook fails the deployment when the shell returns a
non-zero exit code:

```yaml
hooks:
  postprovision:
    posix:
      shell: sh
      run: |
        cosmosdbshell --connect "$COSMOS_ENDPOINT" \
          --connect-subscription "$AZURE_SUBSCRIPTION_ID" \
          --connect-resource-group "$AZURE_RESOURCE_GROUP" \
          -c "seed.csh"
```

### Cache the shell binary

The [setup action](../.github/actions/setup-cosmosdb-shell/README.md) already
caches the extracted binary in the runner tool cache keyed by version and
runtime, so repeated runs on the same version skip the download.
