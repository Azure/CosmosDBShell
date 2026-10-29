# Ingestion sizing, progress, and limitations

Use [`import`](commands.md#import) for files of items (JSON arrays, JSON object sequences/JSON Lines, or CSV). Use [`bulk run`](commands.md#bulk) for operation plans, mixed mutations, or journal-based recovery. Neither command is a transaction: completed writes are not rolled back.

## Choosing a workload size

There is no single recommended maximum file size or completion time. Item size and indexed properties, available RU/s, partition-key distribution, network latency, client resources, and competing traffic all matter. A file's byte count alone is not enough to predict its write cost.

1. Start with **1,000 representative items** on a disposable container with the intended partition key, indexing policy, and throughput. This is a pilot size, not a product limit.
2. Keep the default **concurrency of 16** initially. Compare 32 only if the pilot shows spare capacity; increasing concurrency does not increase RU/s or fix a hot partition. Use 1 for sequential import ordering, accepting lower throughput.
3. Measure elapsed time, successful writes, failures, and observed RU charge. Then try 10,000 items before committing to a much larger job. Repeat representative Azure measurements before publishing customer completion-time estimates.
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

Human-readable import and bulk execution report about every two seconds, even if no writes have completed since the previous update. In a terminal, a single progress line is updated in place and removed when the command finishes, leaving the final summary; when output is piped or redirected by the operating system, each update is written on its own line:

- Import shows completed items, imported/failed counts, elapsed seconds, average completed items/second, and observed write RU charge. Offline validation shows validated items instead.
- Bulk execution shows processed/total operations, succeeded/failed counts, elapsed seconds, average processed operations/second, and observed RU charge. Processed operations can include journal skips, so this is not necessarily a new-write rate. Failures include uncertain outcomes; use the final result/journal to distinguish them.
- Bulk execution progress starts **after** validation/selection, confirmation, and journal verification. These preparation steps can add time before the first update.
- These are cumulative averages, not instantaneous rates. Observed RU charge covers recorded responses, not a billing estimate or a count of internal SDK retries. Unchanged counts can mean input work, pending requests, or SDK retry waits; they do not prove a hang or identify throttling.
- Streamed imports do not pre-count records, so they have no percentage or ETA. Bulk knows its operation count.
- Quiet mode, structured output, shell stdout redirection, and MCP calls do not emit terminal progress. Final structured results retain their existing fields.

## Limits and recovery

- **Emulator inactivity timeout:** commands connected to `localhost` or `127.0.0.1` are canceled, and the shell disconnects, after **10 seconds without progress**. Every response the emulator returns counts as progress, as does local import/bulk work such as validating and spooling operations. Long imports, deletes, queries, exports, and scripts therefore complete as long as the emulator keeps answering. The timer is paused while the shell waits for you at a confirmation prompt or in an external editor. A stalled or unreachable emulator still times out. Earlier writes remain committed. This is an emulator-only safeguard, not an input-size limit or an Azure service limit.
- **Item size:** the NoSQL service limit is **2 MB per item**, measured as the UTF-8 JSON representation, not 2 MB per input file. Import dry runs do not enforce this service limit. See [service limits](https://learn.microsoft.com/azure/cosmos-db/concepts-limits#per-item-limits).
- **File versus in-memory jobs:** import streams its input. File-based `bulk run` validates into a temporary operation spool before writing, so allow temporary-disk capacity as well as input-file space. Stateful `bulk begin`/`add` jobs are limited to **10,000 operations and 64 MiB of canonical JSON**; use `bulk run <file>` for larger plans.
- **Partial failure:** by default, new scheduling stops after an observed write failure, but started writes settle and can succeed afterward. `--continue-on-error` allows more writes; it does not make malformed input safe. Always inspect the final result.
- **Retry safety:** import has no resumable journal. Replaying inserts can conflict; upsert can replace existing items and must not be used merely to hide uncertainty. Reconcile completed writes before retrying. For bulk recovery, keep the same operation plan and journal; do not blindly retry uncertain writes or assume a new journal prevents replay.
- **Not covered by these measurements:** larger documents, CSV/array parsing differences, skewed keys, large indexes, mixed mutations, ETag conflicts, journal overhead, injected failures, memory ceilings, and real Azure throttling/network behavior. No supported maximum file size is established by this matrix.

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
    dotnet test CosmosDBShell.Tests\CosmosDBShell.Tests.csproj --filter 'FullyQualifiedName~IngestionBenchmarkTests'
} finally {
    Remove-Item Env:\COSMOSDB_SHELL_INGESTION_BENCHMARK_CSV
}
```

The CSV is overwritten at the chosen path and flushed after every verified case. It records input bytes, concurrency, elapsed seconds, observed RU charge, rate, and verified item count. Without the environment variable, the benchmark is skipped.

For production planning, repeat representative tests against an explicitly approved Azure test container with the customer's actual indexing, throughput, region, partition distribution, and item sizes. These local measurements cannot establish Azure service capacity.
