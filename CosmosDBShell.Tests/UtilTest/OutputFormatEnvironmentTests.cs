// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.UtilTest;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;

// These tests mutate the process-wide COSMOSDB_SHELL_FORMAT environment variable.
// xUnit parallelizes across test classes and other tests resolve DefaultOutputFormat
// without setting it, so this collection disables parallelization to prevent races.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OutputFormatEnvironmentTestCollection
{
    public const string Name = "Output format environment tests";
}

/// <summary>
/// Covers the <c>COSMOSDB_SHELL_FORMAT</c> fallback: it supplies the session default format
/// when <c>--output</c> is absent, loses to an explicit <c>--output</c>, and never by itself
/// puts the shell into machine mode.
/// </summary>
[Collection(OutputFormatEnvironmentTestCollection.Name)]
public class OutputFormatEnvironmentTests
{
    [Theory]
    [InlineData("csv", OutputFormat.CSV)]
    [InlineData("json", OutputFormat.JSon)]
    [InlineData("table", OutputFormat.Table)]
    [InlineData("TBL", OutputFormat.Table)]
    public void EnvironmentVariable_SuppliesDefaultFormat(string value, OutputFormat expected)
    {
        RunWithFormatEnvironment(value, () =>
        {
            using var shell = ShellInterpreter.CreateInstance();

            Assert.Equal(expected, shell.DefaultOutputFormat);
        });
    }

    [Fact]
    public void GlobalOutputOption_WinsOverEnvironmentVariable()
    {
        RunWithFormatEnvironment("csv", () =>
        {
            using var shell = ShellInterpreter.CreateInstance();
            shell.Options = new Program.CosmosShellOptions { Output = "table" };

            Assert.Equal(OutputFormat.Table, shell.DefaultOutputFormat);
        });
    }

    [Fact]
    public void UnrecognizedEnvironmentVariable_FallsBackToInteractiveDefault()
    {
        RunWithFormatEnvironment("bogus", () =>
        {
            using var shell = ShellInterpreter.CreateInstance();

            Assert.Equal(OutputFormat.User, shell.DefaultOutputFormat);
        });
    }

    [Fact]
    public void EnvironmentVariable_DoesNotEnableMachineMode()
    {
        // Exporting the variable in a shell profile must not silently suppress banners and
        // colors interactively; only --output, --quiet, and -c enter machine mode.
        RunWithFormatEnvironment("json", () =>
        {
            using var shell = ShellInterpreter.CreateInstance();

            Assert.False(shell.IsMachineMode);
        });
    }

    [Fact]
    public void EnvironmentVariable_AppliesInMachineMode()
    {
        RunWithFormatEnvironment("csv", () =>
        {
            using var shell = ShellInterpreter.CreateInstance();
            shell.Options = new Program.CosmosShellOptions { ExecuteAndQuit = "ls" };

            Assert.True(shell.IsMachineMode);
            Assert.Equal(OutputFormat.CSV, shell.DefaultOutputFormat);
        });
    }

    [Fact]
    public void NoEnvironmentVariable_FallsBackToInteractiveDefault()
    {
        RunWithFormatEnvironment(null, () =>
        {
            using var shell = ShellInterpreter.CreateInstance();

            Assert.Equal(OutputFormat.User, shell.DefaultOutputFormat);
        });
    }

    private static void RunWithFormatEnvironment(string? value, Action body)
    {
        var previous = Environment.GetEnvironmentVariable("COSMOSDB_SHELL_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable("COSMOSDB_SHELL_FORMAT", value);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("COSMOSDB_SHELL_FORMAT", previous);
        }
    }
}
