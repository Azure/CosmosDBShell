// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Runtime.ExceptionServices;

internal static class BulkExecutor
{
    public static async Task ExecuteAsync(
        IAsyncEnumerable<BulkOperation> source,
        Func<BulkOperation, CancellationToken, Task<BulkOutcome>> write,
        Func<BulkOutcome, Task> record,
        IReadOnlyDictionary<long, BulkOutcome> previous,
        BulkSummary summary,
        int concurrency,
        double? maxRu,
        bool continueOnError,
        bool retryUncertain,
        CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
        var pending = new List<(BulkOperation Operation, PartitionKey Key, Task<BulkOutcome> Task)>();
        var uncertainItems = new HashSet<(string Id, PartitionKey Key)>();
        var newFailures = 0;
        ExceptionDispatchInfo? failure = null;

        async Task SettleAsync(int index)
        {
            var (operation, key, task) = pending[index];
            pending.RemoveAt(index);
            BulkOutcome result;
            try
            {
                result = await task;
            }
            catch (Exception ex)
            {
                // Any fault can follow a dispatched write. Record uncertainty, drain peers,
                // and rethrow the original exception after all started writes are observed.
                failure ??= ExceptionDispatchInfo.Capture(ex);
                result = BulkOutcome.Create(operation, BulkOutcome.Uncertain, error: ex.Message);
            }

            summary.RequestCharge += result.RequestCharge;
            summary.Processed++;
            if (result.Status == BulkOutcome.Succeeded)
            {
                summary.Succeeded++;
            }
            else
            {
                newFailures++;
                summary.AddFailure(result);
                if (result.Status == BulkOutcome.Uncertain)
                {
                    uncertainItems.Add((operation.Id, key));
                }
            }

            await record(result);
        }

        async Task SettleCompletedAsync()
        {
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].Task.IsCompleted)
                {
                    await SettleAsync(i);
                }
            }
        }

        try
        {
            await foreach (var operation in source.WithCancellation(token))
            {
                var key = BulkOperationNormalizer.ParseKey(operation.PartitionKey);
                await SettleCompletedAsync();

                // Operations on the same item keep their list order.
                while (pending.Count >= concurrency || pending.Any(entry => entry.Operation.Id == operation.Id && entry.Key.Equals(key)))
                {
#pragma warning disable VSTHRD003 // All pending tasks were started by this invocation.
                    await Task.WhenAny(pending.Select(entry => entry.Task)).WaitAsync(token);
#pragma warning restore VSTHRD003
                    await SettleCompletedAsync();
                }

                var budgetExhausted = maxRu.HasValue && summary.RequestCharge >= maxRu.Value;
                if (failure != null || (newFailures > 0 && !continueOnError))
                {
                    summary.ResultIncomplete = true;
                    summary.BudgetExceeded = budgetExhausted;
                    break;
                }

                token.ThrowIfCancellationRequested();
                if (previous.TryGetValue(operation.Index, out var old))
                {
                    if (old.Status == BulkOutcome.Succeeded)
                    {
                        summary.Skipped++;
                        summary.Processed++;
                        continue;
                    }

                    if (old.Status is BulkOutcome.Started or BulkOutcome.Uncertain && !retryUncertain)
                    {
                        summary.Skipped++;
                        summary.Processed++;
                        summary.AddFailure(old with { Status = BulkOutcome.Uncertain });
                        uncertainItems.Add((operation.Id, key));
                        continue;
                    }
                }

                if (!retryUncertain && uncertainItems.Contains((operation.Id, key)))
                {
                    summary.Skipped++;
                    summary.Processed++;
                    summary.ResultIncomplete = true;
                    continue;
                }

                if (budgetExhausted)
                {
                    summary.ResultIncomplete = true;
                    summary.BudgetExceeded = true;
                    break;
                }

                await record(BulkOutcome.Create(operation, BulkOutcome.Started));
                summary.Attempted++;
                pending.Add((operation, key, write(operation, token)));
            }
        }
        catch (Exception ex)
        {
            // Source and journal failures must not bypass settlement of in-flight writes.
            failure ??= ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            // Observe every started write even when reading, cancellation, or journal I/O fails.
            while (pending.Count > 0)
            {
                try
                {
                    await SettleAsync(0);
                }
                catch (Exception ex)
                {
                    // A settlement failure must not leave the remaining writes unobserved.
                    failure ??= ExceptionDispatchInfo.Capture(ex);
                }
            }
        }

        failure?.Throw();
        token.ThrowIfCancellationRequested();
    }
}
