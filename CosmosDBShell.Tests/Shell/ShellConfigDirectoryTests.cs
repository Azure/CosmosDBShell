// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using Azure.Data.Cosmos.Shell.Core;

// The test below mutates the process-wide COSMOSDB_SHELL_CONFIG_DIR environment
// variable. xUnit parallelizes across test classes and many other tests construct
// ShellInterpreter() without an explicit configPath, so this collection disables
// parallelization to prevent races with tests that read that variable.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ShellConfigDirectoryTestCollection
{
    public const string Name = "Shell config directory environment tests";
}

[Collection(ShellConfigDirectoryTestCollection.Name)]
public class ShellConfigDirectoryTests
{
    [Fact]
    public void DisabledHistory_DoesNotLoadExistingCommands()
    {
        var isolatedDir = Path.Join(Path.GetTempPath(), $"cosmosshell-no-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(isolatedDir);
        var historyFile = Path.Join(isolatedDir, "cmd_history");
        File.WriteAllText(historyFile, "echo HISTORY_SENTINEL\n");
        try
        {
            using var shell = new ShellInterpreter(isolatedDir, historyEnabled: false);
            Assert.Empty(shell.History);
            Assert.Equal(0, shell.PendingHistoryCount);
            Assert.Equal("echo HISTORY_SENTINEL\n", File.ReadAllText(historyFile));
        }
        finally
        {
            Directory.Delete(isolatedDir, recursive: true);
        }
    }

    [Fact]
    public void ConfigDirEnvVar_RedirectsHistoryFileAwayFromRealUserDirectory()
    {
        // Guards against process-level tests (for example --clear-history) deleting
        // the developer's real command history: setting COSMOSDB_SHELL_CONFIG_DIR
        // must move the history file into the isolated directory.
        var isolatedDir = Path.Join(Path.GetTempPath(), $"cosmosshell-cfg-{Guid.NewGuid():N}");
        var previous = Environment.GetEnvironmentVariable("COSMOSDB_SHELL_CONFIG_DIR");
        try
        {
            Environment.SetEnvironmentVariable("COSMOSDB_SHELL_CONFIG_DIR", isolatedDir);
            using var shell = new ShellInterpreter();

            Assert.Equal(Path.Join(isolatedDir, "cmd_history"), shell.HistoryFile);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COSMOSDB_SHELL_CONFIG_DIR", previous);
            if (Directory.Exists(isolatedDir))
            {
                Directory.Delete(isolatedDir, recursive: true);
            }
        }
    }
}
