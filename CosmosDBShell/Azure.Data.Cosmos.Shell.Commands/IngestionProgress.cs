// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Diagnostics;
using System.Globalization;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

internal sealed class IngestionProgress(ShellInterpreter shell, CommandState state, string messageKey, long? total = null)
{
    private readonly object sync = new();
    private long processed;
    private long succeeded;
    private long failed;
    private double charge;

    internal static bool IsEnabled(ShellInterpreter shell, CommandState state)
    {
        var format = state.OutputFormatExplicitlySet ? state.OutputFormat : shell.DefaultOutputFormat;
        return !shell.IsMachineMode && !shell.IsMcpInvocation
            && string.IsNullOrEmpty(shell.StdOutRedirect)
            && shell.IsOutputTerminal()
            && !OutputPolicy.IsStructuredFormat(format);
    }

    internal void Update(long processed, long succeeded, long failed, double charge)
    {
        // Display only: failed writes, including SDK-synthesized timeouts, must not count as
        // emulator activity. Real responses restart the inactivity timeout in OperationProgressHandler.
        lock (this.sync)
        {
            this.processed = processed;
            this.succeeded = succeeded;
            this.failed = failed;
            this.charge = charge;
        }
    }

    internal async Task RunAsync(Func<Task> execute)
    {
        if (!IsEnabled(shell, state))
        {
            await execute();
            return;
        }

        // Start reporting before invoking the work so a long synchronous stretch before its first
        // await is still covered; the timer continuations run on the thread pool.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reporting = this.ReportSafelyAsync(completion.Task);
        try
        {
            await execute();
        }
        finally
        {
            completion.TrySetResult();
            await reporting;
            try
            {
                shell.ClearTransientLine();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Like rendering, cleanup must never replace the operation's own outcome.
                Trace.TraceWarning("Clearing the ingestion progress line failed ({0}).", ex.GetType().Name);
            }
        }
    }

    private async Task ReportSafelyAsync(Task completed)
    {
        try
        {
            await this.ReportUntilCompleteAsync(completed);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Progress is informational: a formatting or output failure must not turn a
            // successful ingestion into a failed one. Stop reporting and let the work finish.
            Trace.TraceWarning("Ingestion progress reporting stopped ({0}).", ex.GetType().Name);
        }
    }

    private async Task ReportUntilCompleteAsync(Task completed)
    {
        var elapsed = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var tick = timer.WaitForNextTickAsync().AsTask();
        while (await Task.WhenAny(completed, tick).ConfigureAwait(false) != completed)
        {
            await tick.ConfigureAwait(false);
            string message;
            string compactMessage;
            lock (this.sync)
            {
                object[] args =
                [
                    "processed",
                    this.processed,
                    "succeeded",
                    this.succeeded,
                    "failed",
                    this.failed,
                    "total",
                    total ?? 0,
                    "elapsed",
                    elapsed.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture),
                    "rate",
                    (this.processed / elapsed.Elapsed.TotalSeconds).ToString("F1", CultureInfo.InvariantCulture),
                    "charge",
                    this.charge.ToString("F2", CultureInfo.InvariantCulture),
                ];
                message = MessageService.GetArgsString(messageKey, args);
                compactMessage = MessageService.GetArgsString(messageKey + "-compact", args);
            }

            shell.WriteTransientLine(message, compactMessage);
            tick = timer.WaitForNextTickAsync().AsTask();
        }
    }
}
