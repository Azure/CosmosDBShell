# Ingestion sizing, progress, and limitations

Use [`import`](commands.md#import) for files of items (JSON arrays, JSON object sequences/JSON Lines, or CSV). Use [`bulk run`](commands.md#bulk) for operation plans, mixed mutations, or journal-based recovery. Neither command is a transaction: completed writes are not rolled back.

## Choosing a workload size

There is no single recommended maximum file size or completion time. Item size and indexed properties, available RU/s, partition-key distribution, network latency, client resources, and competing traffic all matter. A file's byte count alone is not enough to predict its write cost.

1. Start with **1,000 representative items** on a disposable container with the intended partition key, indexing policy, and throughput. This is a pilot size, not a product limit.
2. Keep the default **concurrency of 16** initially. Compare 32 only if the pilot shows spare capacity; increasing concurrency does not increase RU/s or fix a hot partition. Use 1 for sequential import ordering, accepting lower throughput.
3. Measure elapsed time, successful writes, failures, and observed RU charge. Then try 10,000 items before committing to a much larger job. The [Azure measurements](#azure-measurements-west-europe) below show one reference configuration; repeat them with your own item shape, indexing, throughput, and region before quoting completion times.
4. Choose chunks around the duration you can afford to restart or reconcile. An initial estimate is `items per chunk = measured items/second * acceptable seconds per chunk`; remeasure when item shape or target settings change.
5. Budget for other workloads. `items * observed RU/item / available RU/s` is only an optimistic throughput-based duration estimate, not a completion-time guarantee. Retries, skew, client limits, and network overhead can make a job slower.

For example:

```text
import pilot.jsonl --dry-run
import pilot.jsonl --concurrency=16

bulk run operations.jsonl --dry-run
bulk run operations.jsonl --concurrency=16 --journal operations.journal --yes
```

`import --dry-run` is offline syntax/ID validation, not a service acceptance or performance test. `bulk --dry-run` validates operations against target metadata; query-driven bulk selection can consume RUs even in a dry run.

## Reading progress

Human-readable import and bulk execution report about every two seconds, even if no writes have completed since the previous update. A single progress line is updated in place and removed when the command finishes, leaving the final summary:

- Import shows completed items, imported/failed counts, elapsed seconds, average completed items/second, and observed write RU charge. Offline validation shows validated items instead.
- Bulk execution shows processed/total operations, succeeded/failed counts, elapsed seconds, average processed operations/second, and observed RU charge. Processed operations can include journal skips, so this is not necessarily a new-write rate. Failures include uncertain outcomes; use the final result/journal to distinguish them.
- Bulk execution progress starts **after** validation/selection, confirmation, and journal verification. These preparation steps can add time before the first update.
- These are cumulative averages, not instantaneous rates. Observed RU charge covers recorded responses, not a billing estimate or a count of internal SDK retries. Unchanged counts can mean input work, pending requests, or SDK retry waits; they do not prove a hang or identify throttling.
- Streamed imports do not pre-count records, so they have no percentage or ETA. Bulk knows its operation count.
- Progress appears only when standard output is a terminal. Quiet mode, structured output, shell or operating-system output redirection, operating-system pipes, and MCP calls do not emit it. In an in-shell `|` pipeline, an earlier stage still shows progress on the terminal, but the line is erased before the next stage runs and never becomes part of the piped data. Final structured results retain their existing fields.

## Limits and recovery

- **Emulator inactivity timeout:** commands connected to `localhost` or `127.0.0.1` are canceled, and the shell disconnects, after **10 seconds without progress**. Every response the emulator returns counts as progress, as does local import/bulk work such as validating and spooling operations. Long imports, deletes, queries, exports, and scripts therefore complete as long as the emulator keeps answering. The timer is paused while the shell waits for you at a confirmation prompt or in an external editor, during intentional `watch` polling intervals, and while local external tools such as `jq` run. A stalled or unreachable emulator still times out. Earlier writes remain committed. This is an emulator-only safeguard, not an input-size limit or an Azure service limit.
- **Item size:** the NoSQL service limit is **2 MB per item**, measured as the UTF-8 JSON representation, not 2 MB per input file. Import dry runs do not enforce this service limit. See [service limits](https://learn.microsoft.com/azure/cosmos-db/concepts-limits#per-item-limits).
- **File versus in-memory jobs:** import streams its input. File-based `bulk run` validates into a temporary operation spool before writing, so allow temporary-disk capacity as well as input-file space. Stateful `bulk begin`/`add` jobs are limited to **10,000 operations and 64 MiB of canonical JSON**; use `bulk run <file>` for larger plans.
- **Partial failure:** by default, new scheduling stops after an observed write failure, but started writes settle and can succeed afterward. `--continue-on-error` allows more writes; it does not make malformed input safe. Always inspect the final result.
- **Retry safety:** import has no resumable journal. Replaying inserts can conflict; upsert can replace existing items and must not be used merely to hide uncertainty. Reconcile completed writes before retrying. For bulk recovery, keep the same operation plan and journal; do not blindly retry uncertain writes or assume a new journal prevents replay.
- **Not covered by these measurements:** larger documents, CSV/array parsing differences, skewed keys, large indexes, mixed mutations, ETag conflicts, journal overhead, injected failures, memory ceilings, multi-region accounts, throttled (429) workloads, and clients far from the account region. No supported maximum file size is established by these matrices.

## Azure measurements (West Europe)

These are measurements against a real Azure Cosmos DB account for one reference configuration. They show the shape of the trade-offs; they are not service guarantees.

Configuration:

- Account `mikkrg` (provisioned throughput), single region **West Europe**, Session consistency, no availability zones. The client ran in Europe (about 55–60 ms HTTPS round trip to the account endpoint) on the same Windows 11 host as the emulator runs.
- Entra ID sign-in (Azure CLI identity); the shell's default **Direct** connection mode for token-based connections. Cosmos .NET SDK 3.63.2; .NET 10.
- One container with partition key `/pk`, default indexing, and **4,000 RU/s manual** throughput. All 18 cases wrote into this container; each case used its own ID prefix and a `caseId` property for verification, and items stayed exactly 1,024 bytes.
- Same workload, matrix, timing boundaries, and verification as the emulator runs: insert/create only, 1,000 evenly used partition-key values, one sample per combination, run sequentially on 2026-10-29.

### Results (2026-10-29)

All 18 cases completed with matching item counts (verified by count query) and zero reported write failures. Times are measured seconds.

| Items | Concurrency | Import (seconds) | Bulk create (seconds) | Rate (items/s) |
|---:|---:|---:|---:|---:|
| 1,000 | 1 | 24.99 | 23.54 | 40–43 |
| 1,000 | 16 | 6.55 | 3.21 | 153–312 |
| 1,000 | 32 | 4.00 | 3.59 | 250–278 |
| 10,000 | 1 | 224.03 | 225.47 | 44–45 |
| 10,000 | 16 | 27.17 | 25.65 | 368–390 |
| 10,000 | 32 | 26.44 | 25.43 | 378–393 |
| 100,000 | 1 | 2,189.28 (36.5 min) | 2,209.65 (36.8 min) | 45–46 |
| 100,000 | 16 | 256.65 (4.3 min) | 255.23 (4.3 min) | 390–392 |
| 100,000 | 32 | 256.22 (4.3 min) | 255.95 (4.3 min) | 390–391 |

What the numbers show:

- **Cost per item:** each 1 KiB insert cost **7.0 RU** with default indexing (700,000 RU for 100,000 items; bulk adds 2 RU for target metadata). Plan RU budgets from measured charges, not from item count alone.
- **Concurrency 1 is latency-bound:** about 45 items/s, roughly 22 ms per write. Sequential ingestion of 100,000 items took about 37 minutes regardless of command. Use it only when file order matters.
- **The default concurrency (16) was about 8.5× faster** than sequential: about 390 items/s, or roughly 1.4 million items per hour, so 100,000 items took about 4.3 minutes. Increasing to 32 gave no further gain.
- **The plateau was not RU throttling.** Azure Monitor recorded no 429 (throttled) responses during the run, and peak normalized RU consumption was 83%. About 390 items/s × 7 RU is roughly 2,730 RU/s, below the provisioned 4,000 RU/s. The limiting factor (for example per-partition write latency or client-side limits) was not isolated, so raising RU/s alone is not guaranteed to raise this rate. Measure before provisioning for a target duration.
- **Small jobs are dominated by fixed overhead.** At 1,000 items, connection warm-up and metadata reads make results noisy; use 10,000 items or more to estimate sustained rates.
- `import` and `bulk create` performed the same for this workload. Choose between them by feature (file formats versus journals and mixed operations), not speed.

### Reproducing the Azure matrix

The opt-in `AzureIngestionBenchmarkTests` signs in with the Azure CLI identity (`az login`), which needs a data-plane write role (for example *Cosmos DB Built-in Data Contributor*) and Azure Resource Manager rights to create and delete databases. It creates one uniquely named database with a container at the configured manual throughput, and deletes the database at the end. **It incurs provisioned-throughput charges for the duration of the run** (about 1 hour 40 minutes for the full matrix in this configuration; 4,000 RU/s in West Europe was about $0.32 per hour at the time of measurement). Run it only against an account you are authorized to use for testing.

```powershell
$env:COSMOSDB_SHELL_AZURE_BENCHMARK_ENDPOINT = 'https://<account>.documents.azure.com:443/'
$env:COSMOSDB_SHELL_AZURE_BENCHMARK_SUBSCRIPTION = '<subscription-id>'
$env:COSMOSDB_SHELL_AZURE_BENCHMARK_RESOURCE_GROUP = '<resource-group>'
$env:COSMOSDB_SHELL_AZURE_BENCHMARK_RU = '4000'   # optional; defaults to 4000
$env:COSMOSDB_SHELL_AZURE_BENCHMARK_CSV = Join-Path $PWD 'azure-ingestion-benchmark.csv'
dotnet test CosmosDBShell.Tests\CosmosDBShell.Tests.csproj --filter 'FullyQualifiedName~AzureIngestionBenchmarkTests'
```

Without these variables the test is skipped. If a run is interrupted, delete any remaining `IngestionBenchmark_*` database.

## Local emulator measurements

These are **local command-path measurements, not Azure throughput promises**.

Configuration:

- Windows 11, 8 logical processors, approximately 16 GiB RAM; shared development host.
- Windows Cosmos DB Emulator 2.14.28.0; Cosmos .NET SDK 3.63.2; .NET 10.
- Gateway connection to the local emulator. Fresh container per case, partition key `/pk`, default indexing, configured manual throughput of 10,000 RU/s.
- Insert/create only, unique IDs, 1,000 evenly used partition-key values. Each item is exactly 1,024 UTF-8 JSON bytes; JSON Lines adds one LF per item. Bulk input also includes the operation envelope.
- One sample per combination, sequentially run; no journal, no terminal progress rendering, no deliberate failure injection, and no controlled cold/warm-cache comparison.
- Timing includes command parsing, metadata access, input parsing, and writes (plus bulk plan validation/spooling). Input generation, container provisioning, independent count verification, and cleanup are outside the timed interval. The parsed command runner is invoked directly, outside the emulator inactivity timeout; SDK request/retry behavior is unchanged.
- Each completed case requires zero reported write failures and an independent count matching the requested number of items.

### Results (2026-10-29)

All 18 cases completed with matching item counts and zero reported write failures. Times below are measured seconds, rounded to two decimal places; they are not extrapolations.

| Items | Concurrency | Import (seconds) | Bulk create (seconds) |
|---:|---:|---:|---:|
| 1,000 | 1 | 15.80 | 15.55 |
| 1,000 | 16 | 5.19 | 5.28 |
| 1,000 | 32 | 5.18 | 5.15 |
| 10,000 | 1 | 155.42 | 155.34 |
| 10,000 | 16 | 51.81 | 51.68 |
| 10,000 | 32 | 51.50 | 51.49 |
| 100,000 | 1 | 1,562.95 | 1,556.85 |
| 100,000 | 16 | 518.49 | 519.02 |
| 100,000 | 32 | 517.23 | 534.51 |

At 100,000 items, input files were 102,500,000 bytes for import (about 97.75 MiB) and 104,800,000 bytes for bulk (about 99.95 MiB). Import reported 6,620 / 66,200 / 662,000 write RUs for the three sizes; bulk reported 2 additional RUs per case for target metadata. Verification and provisioning RUs are not included in these command results.

On this host, concurrency 16 was about three times faster than 1; 32 showed no consistent improvement over 16. This supports keeping 16 as the initial setting, not advertising 32 as automatically faster. A 100,000-item file completed through the command runner, but that is not a maximum-size recommendation. Do not copy these local rates into Azure customer SLAs.

### Reproducing the matrix

The opt-in test creates a uniquely named database on **the local emulator only** and deletes its containers and database afterward. It performs 666,000 writes across 18 cases, so allow substantial time and do not run it alongside other performance tests. Existing emulator data is not reset.

From the repository root in PowerShell:

```powershell
$env:COSMOSDB_SHELL_INGESTION_BENCHMARK_CSV = Join-Path $PWD 'ingestion-benchmark.csv'
try {
    dotnet test CosmosDBShell.Tests\CosmosDBShell.Tests.csproj --filter 'FullyQualifiedName~Integration.IngestionBenchmarkTests'
} finally {
    Remove-Item Env:\COSMOSDB_SHELL_INGESTION_BENCHMARK_CSV
}
```

The CSV is overwritten at the chosen path and flushed after every verified case. It records input bytes, concurrency, elapsed seconds, observed RU charge, rate, and verified item count. Without the environment variable, the benchmark is skipped.

These local measurements cannot establish Azure service capacity; see [Azure measurements](#azure-measurements-west-europe) for a real-account reference, and repeat representative tests with your own indexing, throughput, region, partition distribution, and item sizes.
