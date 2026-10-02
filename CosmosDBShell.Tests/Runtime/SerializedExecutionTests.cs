namespace CosmosShell.Tests.Runtime;

using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
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
            File.WriteAllText(Path.Join(childConfigPath, $"{childPrefix}.ready"), string.Empty);
            var startFile = Path.Join(childConfigPath, "start");
            while (!File.Exists(startFile))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
            }

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
            await WaitForFileAsync(Path.Join(configPath, "first.ready"), timeout.Token);
            await WaitForFileAsync(Path.Join(configPath, "second.ready"), timeout.Token);
            File.WriteAllText(Path.Join(configPath, "start"), string.Empty);

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
            using var constructorStarted = new ManualResetEventSlim();
            using (var lockedStream = new FileStream(historyFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                constructor = Task.Run(
                    () =>
                    {
                        constructorStarted.Set();
                        return new ShellInterpreter(configPath);
                    },
                    TestContext.Current.CancellationToken);
                Assert.True(constructorStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
                Thread.Sleep(TimeSpan.FromMilliseconds(25));
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
    public void Constructor_LoadsReadableHistoryFileWithoutWritePermission()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not apply on Windows.");
            return;
        }

        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configPath);
        var historyFile = Path.Join(configPath, "cmd_history");
        try
        {
            File.WriteAllLines(historyFile, ["echo readable"]);
            File.SetUnixFileMode(historyFile, UnixFileMode.UserRead);

            using var shell = new ShellInterpreter(configPath);
            Assert.Equal(["echo readable"], shell.History);
        }
        finally
        {
            if (File.Exists(historyFile))
            {
                File.SetUnixFileMode(historyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

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
            var historyFile = Path.Join(configPath, "cmd_history");
            shell.PrintCommand("echo retained");

            using (var lockedStream = new FileStream(historyFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Throws<IOException>(() => shell.ClearHistory());
            }

            Assert.Equal(["echo retained"], shell.History);
            Assert.NotEmpty(File.ReadAllLines(historyFile));
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
    public void WriteHistoryAtomically_FailedWritePreservesDestinationAndRemovesTemporaryFile()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configPath);
        var historyFile = Path.Join(configPath, "cmd_history");
        try
        {
            File.WriteAllText(historyFile, "echo retained\n");
            var failure = new IOException("Injected history write failure.");

            var exception = Assert.Throws<IOException>(() => ShellInterpreter.WriteHistoryAtomically(historyFile, stream =>
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.WriteLine("echo partial");
                writer.Flush();
                throw failure;
            }));

            Assert.Same(failure, exception);
            Assert.Equal("echo retained\n", File.ReadAllText(historyFile));
            Assert.Equal([historyFile], Directory.GetFiles(configPath));
        }
        finally
        {
            Directory.Delete(configPath, recursive: true);
        }
    }

    [Fact]
    public void WriteHistoryAtomically_PublishesOnlyAfterWritingCompleteContent()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configPath);
        var historyFile = Path.Join(configPath, "cmd_history");
        try
        {
            File.WriteAllText(historyFile, "echo retained\n");

            ShellInterpreter.WriteHistoryAtomically(historyFile, stream =>
            {
                using var writer = new StreamWriter(stream, leaveOpen: true) { NewLine = "\n" };
                writer.WriteLine("echo first");
                writer.Flush();
                Assert.Equal("echo retained\n", File.ReadAllText(historyFile));
                writer.WriteLine("echo second");
            });

            Assert.Equal("echo first\necho second\n", File.ReadAllText(historyFile));
            Assert.Equal([historyFile], Directory.GetFiles(configPath));
        }
        finally
        {
            Directory.Delete(configPath, recursive: true);
        }
    }

    [Fact]
    public void WriteHistoryAtomically_PreservesWindowsDestinationDaclAtCreationAndPublication()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows ACLs do not apply on this platform.");
            return;
        }

        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configPath);
        var historyFile = Path.Join(configPath, "cmd_history");
        try
        {
            File.WriteAllText(historyFile, "echo retained\n");
            using var identity = WindowsIdentity.GetCurrent();
            var owner = Assert.IsType<SecurityIdentifier>(identity.User);
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.Read, AccessControlType.Allow));
            new FileInfo(historyFile).SetAccessControl(security);
            var expected = new FileInfo(historyFile).GetAccessControl(AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access);

            ShellInterpreter.WriteHistoryAtomically(historyFile, stream =>
            {
                AssertWindowsHistoryDacl(Assert.Single(Directory.GetFiles(configPath, "cmd_history.*.tmp")), expected);
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.WriteLine("echo replacement");
            });

            AssertWindowsHistoryDacl(historyFile, expected);
            Assert.Equal(["echo replacement"], File.ReadAllLines(historyFile));
        }
        finally
        {
            Directory.Delete(configPath, recursive: true);
        }
    }

    [Fact]
    public void WriteHistoryAtomically_NewWindowsHistoryHasOwnerOnlyDaclAtCreationAndPublication()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows ACLs do not apply on this platform.");
            return;
        }

        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configPath);
        var historyFile = Path.Join(configPath, "cmd_history");
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var owner = Assert.IsType<SecurityIdentifier>(identity.User);
            var expected = new FileSecurity();
            expected.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            expected.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            var expectedDacl = expected.GetSecurityDescriptorSddlForm(AccessControlSections.Access);

            ShellInterpreter.WriteHistoryAtomically(historyFile, stream =>
            {
                AssertWindowsHistoryDacl(Assert.Single(Directory.GetFiles(configPath, "cmd_history.*.tmp")), expectedDacl);
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.WriteLine("echo private");
            });

            AssertWindowsHistoryDacl(historyFile, expectedDacl);
            Assert.Equal(["echo private"], File.ReadAllLines(historyFile));
        }
        finally
        {
            Directory.Delete(configPath, recursive: true);
        }
    }

    [Fact]
    public void PrintCommand_FailedSavesPreserveHistoryAndBoundPendingEntries()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        try
        {
            using var shell = new ShellInterpreter(configPath);
            shell.PrintCommand("echo retained");
            var lockFile = shell.HistoryFile + ".lock";
            File.Delete(lockFile);
            Directory.CreateDirectory(lockFile);

            for (var index = 0; index < 70; index++)
            {
                shell.PrintCommand($"echo pending-{index}");
            }

            Assert.Equal(60, shell.PendingHistoryCount);
            Assert.Equal(60, shell.History.Count);
            Assert.Equal(["echo retained"], File.ReadAllLines(shell.HistoryFile));

            Directory.Delete(lockFile);
            shell.PrintCommand("echo recovered");

            Assert.Equal(0, shell.PendingHistoryCount);
            using var restarted = new ShellInterpreter(configPath);
            Assert.Equal(60, restarted.History.Count);
            Assert.Equal("echo pending-11", restarted.History[0]);
            Assert.Equal("echo recovered", restarted.History[^1]);
        }
        finally
        {
            Directory.Delete(configPath, recursive: true);
        }
    }

    [Fact]
    public void ClearHistory_UsesSharedLockAndPreservesPendingEntriesOnFailure()
    {
        var configPath = Path.Join(Path.GetTempPath(), $"cosmosshell-history-{Guid.NewGuid():N}");
        try
        {
            using var shell = new ShellInterpreter(configPath);
            shell.PrintCommand("echo retained");
            using (var locked = new FileStream(shell.HistoryFile + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                shell.PrintCommand("echo pending");
                Assert.Throws<IOException>(() => shell.ClearHistory());
                Assert.Equal(1, shell.PendingHistoryCount);
                Assert.Equal(["echo retained", "echo pending"], shell.History);
                Assert.Equal(["echo retained"], File.ReadAllLines(shell.HistoryFile));
            }

            shell.ClearHistory();
            Assert.Equal(0, shell.PendingHistoryCount);
            Assert.Empty(shell.History);
            Assert.Empty(File.ReadAllLines(shell.HistoryFile));
        }
        finally
        {
            Directory.Delete(configPath, recursive: true);
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

    private static void AssertWindowsHistoryDacl(string path, string expectedDacl)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows ACLs do not apply on this platform.");
        }

        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        Assert.True(security.AreAccessRulesProtected);
        var expected = Assert.IsType<RawAcl>(new RawSecurityDescriptor(expectedDacl).DiscretionaryAcl);
        var actual = Assert.IsType<RawAcl>(new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0).DiscretionaryAcl);
        var expectedBytes = new byte[expected.BinaryLength];
        var actualBytes = new byte[actual.BinaryLength];
        expected.GetBinaryForm(expectedBytes, 0);
        actual.GetBinaryForm(actualBytes, 0);
        Assert.Equal(expectedBytes, actualBytes);
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

    private static async Task WaitForFileAsync(string path, CancellationToken cancellationToken)
    {
        while (!File.Exists(path))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
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