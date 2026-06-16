// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Microsoft.Azure.Cosmos;

using Xunit;

namespace CosmosShell.Tests.Runtime;

public class DiagnosticLogTests : IDisposable
{
    private readonly string path;

    public DiagnosticLogTests()
    {
        this.path = Path.Combine(Path.GetTempPath(), $"cosmosdbshell-diag-{Guid.NewGuid():N}.log");
    }

    public void Dispose()
    {
        if (File.Exists(this.path))
        {
            File.Delete(this.path);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Create_WritesSessionMetadataHeader()
    {
        using (DiagnosticLog.Create(this.path))
        {
        }

        var lines = File.ReadAllLines(this.path);

        Assert.Equal("# CosmosDB Shell Diagnostic Log", lines[0]);
        Assert.StartsWith("# Started: ", lines[1]);
        Assert.StartsWith("# Machine: ", lines[2]);
        Assert.StartsWith("# OS: ", lines[3]);
        Assert.StartsWith("# Runtime: ", lines[4]);
        Assert.Equal(new string('-', 80), lines[5]);
    }

    [Fact]
    public void LogConnect_WritesEndpointAndMode()
    {
        using (var log = DiagnosticLog.Create(this.path))
        {
            log.LogConnect(new Uri("https://myaccount.documents.azure.com:443/"), ConnectionMode.Direct);
        }

        var line = LastEntry();
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\.\d{3}\] \[CONNECT \] https://myaccount\.documents\.azure\.com:443/ \(mode=Direct\)$", line);
    }

    [Fact]
    public void LogCommand_WritesCommandText()
    {
        using (var log = DiagnosticLog.Create(this.path))
        {
            log.LogCommand("dir");
        }

        var line = LastEntry();
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\.\d{3}\] \[CMD     \] dir$", line);
    }

    [Fact]
    public void LogResult_Success_WritesOkWithElapsed()
    {
        using (var log = DiagnosticLog.Create(this.path))
        {
            log.LogResult(succeeded: true, elapsedMilliseconds: 333.25, command: "dir");
        }

        var line = LastEntry();
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\.\d{3}\] \[RESULT  \] \[OK\] 333\.[23]ms \| dir$", line);
    }

    [Fact]
    public void LogResult_Failure_WritesFail()
    {
        using (var log = DiagnosticLog.Create(this.path))
        {
            log.LogResult(succeeded: false, elapsedMilliseconds: 2.1, command: "cat nonexistent");
        }

        var line = LastEntry();
        Assert.Contains("[RESULT  ] [FAIL] 2.1ms | cat nonexistent", line);
    }

    [Fact]
    public void LogError_WritesExceptionTypeAndMessage()
    {
        using (var log = DiagnosticLog.Create(this.path))
        {
            log.LogError("cat nonexistent", new InvalidOperationException("Item not found"));
        }

        var line = LastEntry();
        Assert.Contains("[ERROR   ] cat nonexistent -> InvalidOperationException: Item not found", line);
    }

    [Fact]
    public void LogCommand_FlattensMultiLineCommand()
    {
        using (var log = DiagnosticLog.Create(this.path))
        {
            log.LogCommand("line one\r\nline two");
        }

        var line = LastEntry();
        Assert.EndsWith("[CMD     ] line one line two", line);
    }

    [Fact]
    public void LogCommand_RedactsAccountKey()
    {
        using (var log = DiagnosticLog.Create(this.path))
        {
            log.LogCommand("connect \"AccountEndpoint=https://acc.documents.azure.com:443/;AccountKey=SuperSecretKey123==;\"");
        }

        var line = LastEntry();
        Assert.DoesNotContain("SuperSecretKey123", line);
        Assert.Contains("AccountKey=***", line);
        Assert.Contains("AccountEndpoint=https://acc.documents.azure.com:443/", line);
    }

    [Fact]
    public void LogCancelled_WritesCancelledStatus()
    {
        using (var log = DiagnosticLog.Create(this.path))
        {
            log.LogResult(succeeded: true, elapsedMilliseconds: 5.0, command: "noop");
            log.LogCancelled(elapsedMilliseconds: 12.5, command: "long-running");
        }

        var line = LastEntry();
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\.\d{3}\] \[RESULT  \] \[CANCELLED\] 12\.5ms \| long-running$", line);
    }

    private string LastEntry()
    {
        return File.ReadAllLines(this.path).Last(l => l.StartsWith('['));
    }
}

public class DiagnosticLogInterpreterTests : IDisposable
{
    private readonly string path;

    public DiagnosticLogInterpreterTests()
    {
        this.path = Path.Combine(Path.GetTempPath(), $"cosmosdbshell-diag-{Guid.NewGuid():N}.log");
    }

    public void Dispose()
    {
        if (File.Exists(this.path))
        {
            File.Delete(this.path);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ExecuteCommandAsync_WhenDiagnosticsEnabled_WritesCommandAndResult()
    {
        var interpreter = new ShellInterpreter();
        interpreter.EnableDiagnostics(this.path);

        await interpreter.ExecuteCommandAsync("$x = 1", CancellationToken.None);
        interpreter.Dispose();

        var entries = File.ReadAllLines(this.path).Where(l => l.StartsWith('[')).ToArray();
        Assert.Contains(entries, l => l.Contains("[CMD     ] $x = 1"));
        Assert.Contains(entries, l => l.Contains("[RESULT  ] [OK]") && l.Contains("$x = 1"));
    }

    [Fact]
    public async Task ExecuteCommandAsync_WhenCanceled_DoesNotLogResultAsOk()
    {
        var interpreter = new ShellInterpreter();
        interpreter.EnableDiagnostics(this.path);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await interpreter.ExecuteCommandAsync("$x = 1", cts.Token);
        interpreter.Dispose();

        var entries = File.ReadAllLines(this.path).Where(l => l.StartsWith('[')).ToArray();
        var resultEntries = entries.Where(l => l.Contains("[RESULT  ]")).ToArray();
        Assert.NotEmpty(resultEntries);
        Assert.All(resultEntries, l => Assert.DoesNotContain("[OK]", l));
        Assert.Contains(resultEntries, l => l.Contains("[CANCELLED]"));
    }
}

