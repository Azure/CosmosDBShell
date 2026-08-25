# Setup Azure Cosmos DB Shell (GitHub Action)

A composite GitHub Action that downloads, caches, and adds the Azure Cosmos DB
Shell (`cosmosdbshell`) to `PATH` on Linux, macOS, and Windows runners. No .NET
SDK is required on the runner — the self-contained executable is extracted from
the per-runtime release package published on each GitHub Release.

## Runner prerequisites

The action's steps run under PowerShell (`shell: pwsh`) and use the GitHub CLI
(`gh`) to resolve and download release assets. GitHub-hosted runners include
both by default. On self-hosted runners, ensure PowerShell 7+ and `gh` are
installed (see [PowerShell](https://learn.microsoft.com/powershell/) and
[GitHub CLI](https://cli.github.com/)); otherwise the action fails early with an
actionable message.

## Usage

```yaml
steps:
  - uses: Azure/CosmosDBShell/.github/actions/setup-cosmosdb-shell@main
    with:
      version: latest # or a pinned tag such as v1.1.115

  - name: Smoke test
    run: cosmosdbshell --version
```

> **Pin for reproducibility.** The example above uses `@main` for readability,
> but `main` is a moving reference. For reproducible and supply-chain-safe
> workflows, pin the action to a release tag or a commit SHA, and pin the
> installed shell `version` to an explicit tag:
>
> ```yaml
> steps:
>   - uses: Azure/CosmosDBShell/.github/actions/setup-cosmosdb-shell@v1.1.115
>     with:
>       version: v1.1.115
> ```

Once published to the Marketplace as a standalone repository (see below), the
same action is consumed as:

```yaml
steps:
  - uses: azure/setup-cosmosdb-shell@v1
    with:
      version: 1.1.115
```

## Inputs

| Name      | Default             | Description                                                                                       |
| --------- | ------------------- | ------------------------------------------------------------------------------------------------- |
| `version` | `latest`            | Release to install. `latest` picks the newest release; otherwise pin a tag (a leading `v` is optional, e.g. `v1.1.115` or `1.1.115`). |
| `token`   | `''` (empty)        | Token used for the releases API and asset download. When empty, the action falls back to the workflow token (`github.token`) via `GH_TOKEN: ${{ inputs.token || github.token }}`, so `gh` stays authenticated and avoids unauthenticated rate limits. |

## Outputs

| Name      | Description                                                     |
| --------- | ------------------------------------------------------------- |
| `version` | The resolved release tag that was installed (e.g. `v1.1.115`). |
| `path`    | Directory containing the `cosmosdbshell` executable on `PATH`. |

## How it works

1. Maps the runner OS/architecture to a runtime identifier (`win-x64`,
   `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`).
2. Resolves the release tag (`latest` reads the newest non-draft release).
3. Restores the extracted binary from the runner tool cache via
   [`actions/cache`](https://github.com/actions/cache), keyed by tag + runtime.
4. On a cache miss, finds the matching `CosmosDBShell.<rid>.<version>.nupkg`
   release asset through the GitHub API, downloads it, verifies its SHA256
   digest when the release asset exposes one, extracts the self-contained
   executable, and adds it to `PATH` as `cosmosdbshell`.
5. Verifies the install with `cosmosdbshell --version`.

## Publishing to the Marketplace as `azure/setup-cosmosdb-shell`

Marketplace actions must live at the root of their own repository. To promote
this action:

1. Create a new `azure/setup-cosmosdb-shell` repository.
2. Copy this directory's `action.yml` and `README.md` to the repository root.
3. Tag a release (for example `v1`) and publish it to the GitHub Marketplace.
4. Maintain a moving major tag (`v1`) so consumers can use `@v1`.
