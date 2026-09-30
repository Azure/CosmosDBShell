namespace CosmosShell.Tests.Runtime;

using System.Diagnostics;
using Azure.Data.Cosmos.Shell.Core;

public class SerializedExecutionTests
{
    [Fact]
    public async Task PrintCommand_ConcurrentWithHistorySnapshots_DoesNotCorruptHistory()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var writer = Task.Run(
            () =>
            {
                for (var i = 0; i < 500; i++)
                {
                    shell.PrintCommand($"echo {i}");
                }
            },
            cancellation.Token);

        var reader = Task.Run(
            () =>
            {
                while (!writer.IsCompleted)
                {
                    foreach (var entry in shell.History)
                    {
                        Assert.NotNull(entry);
                    }
                }
            },
            cancellation.Token);

        await Task.WhenAll(writer, reader);
        Assert.Equal("echo 499", shell.History[^1]);
    }

    [Fact]
    public void PrintCommand_PersistsBoundedHistory()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        try
        {
            using (var shell = new ShellInterpreter(configPath))
            {
                for (var i = 0; i < 70; i++)
                {
                    shell.PrintCommand($"echo {i}");
                }
            }

            var persisted = File.ReadAllLines(Path.Join(configPath, "cmd_history"));
            Assert.Equal(60, persisted.Length);
            Assert.Equal("echo 69", persisted[^1]);

            using var restarted = new ShellInterpreter(configPath);
            Assert.Equal("echo 69", restarted.History[^1]);
        }
        finally
        {
            if (Directory.Exists(configPath))
            {
                Directory.Delete(configPath, recursive: true);
            }
        }
    }

    [Fact]
    public void PrintCommand_MergesHistorySavedByMultipleInterpreters()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        try
        {
            using var first = new ShellInterpreter(configPath);
            using var second = new ShellInterpreter(configPath);

            first.PrintCommand("echo from-first");
            second.PrintCommand("echo from-second");

            var persisted = File.ReadAllLines(Path.Join(configPath, "cmd_history"));
            Assert.Equal(["echo from-first", "echo from-second"], persisted);

            using var restarted = new ShellInterpreter(configPath);
            Assert.Equal(["echo from-first", "echo from-second"], restarted.History);
        }
        finally
        {
            if (Directory.Exists(configPath))
            {
                Directory.Delete(configPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PrintCommand_MergesHistorySavedByConcurrentProcesses()
    {
        var childConfigPath = Environment.GetEnvironmentVariable("COSMOSDBSHELL_TEST_HISTORY_CONFIG");
        var childPrefix = Environment.GetEnvironmentVariable("COSMOSDBSHELL_TEST_HISTORY_PREFIX");
        if (!string.IsNullOrEmpty(childConfigPath) && !string.IsNullOrEmpty(childPrefix))
        {
            using var shell = new ShellInterpreter(childConfigPath);
            for (var index = 0; index < 20; index++)
            {
                shell.PrintCommand($"echo {childPrefix}-{index}");
            }

            return;
        }

        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-process-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configPath);
        using var first = CreateHistoryWriterProcess(configPath, "first");
        using var second = CreateHistoryWriterProcess(configPath, "second");
        var firstStarted = false;
        var secondStarted = false;
        try
        {
            firstStarted = first.Start();
            secondStarted = second.Start();
            Assert.True(firstStarted);
            Assert.True(secondStarted);

            var firstOutput = first.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var firstError = first.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            var secondOutput = second.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var secondError = second.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            await Task.WhenAll(
                first.WaitForExitAsync(timeout.Token),
                second.WaitForExitAsync(timeout.Token));

            var output = await Task.WhenAll(firstOutput, firstError, secondOutput, secondError);
            Assert.True(first.ExitCode == 0, $"First process exited with {first.ExitCode}: {output[0]} {output[1]}");
            Assert.True(second.ExitCode == 0, $"Second process exited with {second.ExitCode}: {output[2]} {output[3]}");

            using var restarted = new ShellInterpreter(configPath);
            Assert.Equal(40, restarted.History.Count);
            for (var index = 0; index < 20; index++)
            {
                Assert.Contains($"echo first-{index}", restarted.History);
                Assert.Contains($"echo second-{index}", restarted.History);
            }
        }
        finally
        {
            StopProcess(first, firstStarted);
            StopProcess(second, secondStarted);
            Directory.Delete(configPath, recursive: true);
        }
    }

    [Fact]
    public async Task Constructor_RetriesWhenHistoryFileIsLocked()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configPath);
        var historyFile = Path.Join(configPath, "cmd_history");
        await File.WriteAllLinesAsync(historyFile, ["echo existing"], TestContext.Current.CancellationToken);
        try
        {
            Task<ShellInterpreter> constructor;
            using (var lockedStream = new FileStream(historyFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                constructor = Task.Run(() => new ShellInterpreter(configPath), TestContext.Current.CancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(75), TestContext.Current.CancellationToken);
                Assert.False(constructor.IsCompleted);
            }

            using var shell = await constructor;
            Assert.Equal(["echo existing"], shell.History);
        }
        finally
        {
            Directory.Delete(configPath, recursive: true);
        }
    }

    [Fact]
    public void PrintCommand_DeduplicatesMergedHistoryAcrossInterpreters()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        try
        {
            using var first = new ShellInterpreter(configPath);
            using var second = new ShellInterpreter(configPath);

            first.PrintCommand("echo first-only");
            second.PrintCommand("echo second-only");
            first.PrintCommand("echo shared");
            second.PrintCommand("echo shared");

            using var restarted = new ShellInterpreter(configPath);
            Assert.Equal(["echo first-only", "echo second-only", "echo shared"], restarted.History);
        }
        finally
        {
            if (Directory.Exists(configPath))
            {
                Directory.Delete(configPath, recursive: true);
            }
        }
    }

    [Fact]
    public void PrintCommand_TrimsMergedHistoryToNewestEntries()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        try
        {
            using var first = new ShellInterpreter(configPath);
            using var second = new ShellInterpreter(configPath);

            for (var i = 0; i < 30; i++)
            {
                first.PrintCommand($"echo first-{i}");
            }

            for (var i = 0; i < 40; i++)
            {
                second.PrintCommand($"echo second-{i}");
            }

            using var restarted = new ShellInterpreter(configPath);
            Assert.Equal(60, restarted.History.Count);
            Assert.Equal("echo first-10", restarted.History[0]);
            Assert.Equal("echo first-29", restarted.History[19]);
            Assert.Equal("echo second-0", restarted.History[20]);
            Assert.Equal("echo second-39", restarted.History[^1]);
            Assert.DoesNotContain("echo first-0", restarted.History);
        }
        finally
        {
            if (Directory.Exists(configPath))
            {
                Directory.Delete(configPath, recursive: true);
            }
        }
    }

    [Fact]
    public void PrintCommand_DoesNotResurrectSavedHistoryAfterClear()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        try
        {
            using var shell = new ShellInterpreter(configPath);
            var historyFile = Path.Join(configPath, "cmd_history");

            shell.PrintCommand("echo before-clear");
            shell.ClearHistory();
            Assert.Empty(File.ReadAllLines(historyFile));

            using (var restartedAfterClear = new ShellInterpreter(configPath))
            {
                Assert.Empty(restartedAfterClear.History);
            }

            shell.PrintCommand("echo after-clear");

            using var restartedAfterSave = new ShellInterpreter(configPath);
            Assert.Equal(["echo after-clear"], restartedAfterSave.History);
        }
        finally
        {
            if (Directory.Exists(configPath))
            {
                Directory.Delete(configPath, recursive: true);
            }
        }
    }

    [Fact]
    public void ClearHistory_WhenHistoryFileIsLocked_PreservesLoadedHistory()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        try
        {
            using var shell = new ShellInterpreter(configPath);
            shell.PrintCommand("echo retained");

            using var lockedStream = new FileStream(
                shell.HistoryFile,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            Assert.Throws<IOException>(shell.ClearHistory);
            Assert.Equal(["echo retained"], shell.History);
        }
        finally
        {
            Directory.Delete(configPath, recursive: true);
        }
    }

    [Fact]
    public void PrintCommand_PreservesMultilineHistoryWhenMergingInterpreters()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        try
        {
            using var first = new ShellInterpreter(configPath);
            using var second = new ShellInterpreter(configPath);
            var multiline = "query\nselect * from c";

            first.PrintCommand(multiline);
            second.PrintCommand("echo after-multiline");

            var persisted = File.ReadAllLines(Path.Join(configPath, "cmd_history"));
            Assert.Equal(2, persisted.Length);
            Assert.Equal(multiline, ShellInterpreter.DecodeHistoryLine(persisted[0]));

            using var restarted = new ShellInterpreter(configPath);
            Assert.Equal([multiline, "echo after-multiline"], restarted.History);
        }
        finally
        {
            if (Directory.Exists(configPath))
            {
                Directory.Delete(configPath, recursive: true);
            }
        }
    }

    [Fact]
    public void PrintCommand_RestrictsHistoryFileToOwnerOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not apply on Windows.");
            return;
        }

        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(configPath);
            var historyFile = Path.Join(configPath, "cmd_history");
            File.WriteAllText(historyFile, string.Empty);
            File.SetUnixFileMode(historyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            using (var shell = new ShellInterpreter(configPath))
            {
                shell.PrintCommand("echo 1");
            }

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(historyFile));

            File.Delete(historyFile);
            using (var shell = new ShellInterpreter(configPath))
            {
                shell.PrintCommand("echo 2");
            }

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(historyFile));
        }
        finally
        {
            if (Directory.Exists(configPath))
            {
                Directory.Delete(configPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Dispose_ReleasesExecutionGateAndIsIdempotent()
    {
        var shell = ShellInterpreter.CreateInstance();
        Assert.Equal(42, await shell.RunSerializedAsync(() => Task.FromResult(42), TestContext.Current.CancellationToken));
        shell.Dispose();
        shell.Dispose();
        var executed = false;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => shell.RunSerializedAsync(() =>
        {
            executed = true;
            return Task.FromResult(1);
        }, CancellationToken.None));
        Assert.False(executed);
    }

    [Fact]
    public async Task RunSerializedAsync_SerializesWorkStartedInsideAnOwnedOperation()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var childEntered = false;
        Task<int> child = Task.FromResult(0);
        var first = shell.RunSerializedAsync(
            async () =>
            {
                child = Task.Run(() => shell.RunSerializedAsync(
                    () =>
                    {
                        childEntered = true;
                        return Task.FromResult(7);
                    },
                    CancellationToken.None));
                entered.SetResult();
                await release.Task;
                return 42;
            },
            CancellationToken.None);
        await entered.Task;
        try
        {
            Assert.False(childEntered);
            Assert.False(child.IsCompleted);
        }
        finally
        {
            release.SetResult();
        }

        Assert.Equal(42, await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(7, await child.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunSerializedAsync_WaitsForOtherExecution()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = shell.RunSerializedAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
            return 42;
        }, CancellationToken.None);
        await entered.Task;
        var secondEntered = false;
        var second = shell.RunSerializedAsync(() =>
        {
            secondEntered = true;
            return Task.FromResult(7);
        }, CancellationToken.None);
        try
        {
            Assert.False(secondEntered);
            Assert.False(second.IsCompleted);
        }
        finally
        {
            release.SetResult();
        }

        Assert.Equal(42, await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(7, await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunSerializedAsync_ReleasesGateAfterFailure()
    {
        using var shell = ShellInterpreter.CreateInstance();
        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.RunSerializedAsync<int>(
            () => throw new InvalidOperationException(), CancellationToken.None));
        Assert.Equal(42, await shell.RunSerializedAsync(() => Task.FromResult(42), CancellationToken.None));
    }

    [Fact]
    public async Task RunSerializedAsync_CancelledWaiterDoesNotExecuteOrReleaseAnotherOwnersGate()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var cancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = shell.RunSerializedAsync(async () =>
        {
            await release.Task;
            return 1;
        }, TestContext.Current.CancellationToken);
        var executed = false;
        var waiter = shell.RunSerializedAsync(() =>
        {
            executed = true;
            return Task.FromResult(2);
        }, cancellation.Token);
        try
        {
            await cancellation.CancelAsync();
            try
            {
                await waiter;
                Assert.Fail("The queued operation should have been cancelled.");
            }
            catch (OperationCanceledException)
            {
                Assert.True(cancellation.IsCancellationRequested);
            }

            Assert.False(executed);
            Assert.False(first.IsCompleted);
        }
        finally
        {
            release.SetResult();
            await first;
        }

        Assert.Equal(3, await shell.RunSerializedAsync(() => Task.FromResult(3), TestContext.Current.CancellationToken));
    }

    private static Process CreateHistoryWriterProcess(string configPath, string prefix)
    {
        var testAssembly = typeof(SerializedExecutionTests).Assembly.Location;
        var startInfo = new ProcessStartInfo
        {
            FileName = GetDotnetPath(),
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(testAssembly);
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add($"FullyQualifiedName={typeof(SerializedExecutionTests).FullName}.PrintCommand_MergesHistorySavedByConcurrentProcesses");
        startInfo.Environment["COSMOSDBSHELL_TEST_HISTORY_CONFIG"] = configPath;
        startInfo.Environment["COSMOSDBSHELL_TEST_HISTORY_PREFIX"] = prefix;
        return new Process { StartInfo = startInfo };
    }

    private static string GetDotnetPath()
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return !string.IsNullOrEmpty(dotnet) && File.Exists(dotnet)
            ? dotnet
            : OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    }

    private static void StopProcess(Process process, bool started)
    {
        if (started && !process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }
}