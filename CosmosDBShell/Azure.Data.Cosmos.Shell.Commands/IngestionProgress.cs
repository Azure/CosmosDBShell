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
        lock (this.sync)
        {
            this.processed = processed;
            this.succeeded = succeeded;
            this.failed = failed;
            this.charge = charge;
        }

        shell.ReportOperationProgress();
    }

    internal async Task RunAsync(Func<Task> execute)
    {
        if (!IsEnabled(shell, state))
        {
            await execute();
            return;
        }

        var operation = execute();
        try
        {
            await this.ReportUntilCompleteAsync(operation);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Progress is informational: a formatting or output failure must not turn a
            // successful ingestion into a failed one. Stop reporting and keep waiting.
            Trace.TraceWarning("Ingestion progress reporting stopped ({0}).", ex.GetType().Name);
        }

        try
        {
            await operation;
        }
        finally
        {
            try
            {
                shell.ClearTransientLine();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                Trace.TraceWarning("Clearing the ingestion progress line failed ({0}).", ex.GetType().Name);
            }
        }
    }

    private async Task ReportUntilCompleteAsync(Task operation)
    {
        var elapsed = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var tick = timer.WaitForNextTickAsync().AsTask();
        while (await Task.WhenAny(operation, tick) != operation)
        {
            await tick;
            string message;
            lock (this.sync)
            {
                message = MessageService.GetArgsString(
                    messageKey,
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
                    this.charge.ToString("F2", CultureInfo.InvariantCulture));
            }

            shell.WriteTransientLine(message);
            tick = timer.WaitForNextTickAsync().AsTask();
        }
    }
}
