namespace CosmosShell.Tests.Runtime;

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
}