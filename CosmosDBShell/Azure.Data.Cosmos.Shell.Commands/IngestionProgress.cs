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

        var elapsed = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var operation = execute();
        try
        {
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

                if (shell.IsOutputTerminal())
                {
                    shell.WriteTransientLine(message);
                }
                else
                {
                    ShellInterpreter.WriteLine(message);
                }

                tick = timer.WaitForNextTickAsync().AsTask();
            }
        }
        finally
        {
            try
            {
                // Do not let a reporting failure abandon writes that are still settling.
                await operation;
            }
            finally
            {
                shell.ClearTransientLine();
            }
        }
    }
}
