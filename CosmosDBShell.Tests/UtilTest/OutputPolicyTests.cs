// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.UtilTest;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Behavior matrix for the output-format and machine-mode policy. Covers the
/// format vocabulary (user/json/csv/table) crossed with --quiet, -c
/// (execute-and-quit), stdout redirection, and per-command --format. This locks in
/// the single-source-of-truth resolution shared by <see cref="OutputPolicy"/>,
/// <see cref="ShellInterpreter"/>, and <see cref="InfoCommand"/>.
/// </summary>
public class OutputPolicyTests
{
    [Theory]
    [InlineData(OutputFormat.JSon, true)]
    [InlineData(OutputFormat.CSV, true)]
    [InlineData(OutputFormat.User, false)]
    [InlineData(OutputFormat.Table, false)]
    public void IsStructuredFormat_ClassifiesFormats(OutputFormat format, bool expected)
    {
        Assert.Equal(expected, OutputPolicy.IsStructuredFormat(format));
    }

    [Theory]
    // No format, interactive: not machine mode.
    [InlineData(null, false, false, false)]
    // --quiet always wins.
    [InlineData(null, true, false, true)]
    [InlineData("user", true, false, true)]
    [InlineData("table", true, false, true)]
    // Structured formats are machine mode.
    [InlineData("json", false, false, true)]
    [InlineData("csv", false, false, true)]
    [InlineData("js", false, false, true)]
    // Human-facing formats are not machine mode.
    [InlineData("table", false, false, false)]
    [InlineData("tbl", false, false, false)]
    [InlineData("user", false, false, false)]
    // Execute-and-quit defaults to machine mode unless a human format is requested.
    [InlineData(null, false, true, true)]
    [InlineData("table", false, true, false)]
    [InlineData("user", false, true, false)]
    [InlineData("csv", false, true, true)]
    // Unrecognized token: only machine mode when non-interactive (-c).
    [InlineData("bogus", false, false, false)]
    [InlineData("bogus", false, true, true)]
    public void IsMachineMode_Matrix(string? output, bool quiet, bool executeAndQuit, bool expected)
    {
        Assert.Equal(expected, OutputPolicy.IsMachineMode(output, quiet, executeAndQuit));
    }

    [Theory]
    [InlineData("json", OutputFormat.JSon)]
    [InlineData("csv", OutputFormat.CSV)]
    [InlineData("table", OutputFormat.Table)]
    [InlineData("user", OutputFormat.User)]
    public void DefaultOutputFormat_UsesExplicitGlobalOutput(string output, OutputFormat expected)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = output };

        Assert.Equal(expected, shell.DefaultOutputFormat);
    }

    [Fact]
    public void DefaultOutputFormat_NoOptions_IsUser()
    {
        using var shell = ShellInterpreter.CreateInstance();

        Assert.Equal(OutputFormat.User, shell.DefaultOutputFormat);
    }

    [Fact]
    public void DefaultOutputFormat_QuietWithoutFormat_FallsBackToJson()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Quiet = true };

        Assert.True(shell.IsMachineMode);
        Assert.Equal(OutputFormat.JSon, shell.DefaultOutputFormat);
    }

    [Fact]
    public void DefaultOutputFormat_ExecuteAndQuitWithoutFormat_FallsBackToJson()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { ExecuteAndQuit = "ls" };

        Assert.True(shell.IsMachineMode);
        Assert.Equal(OutputFormat.JSon, shell.DefaultOutputFormat);
    }

    [Theory]
    [InlineData("table", false)]
    [InlineData("user", false)]
    [InlineData("json", true)]
    [InlineData("csv", true)]
    public void ShellIsMachineMode_ReflectsGlobalOutput(string output, bool expected)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = output };

        Assert.Equal(expected, shell.IsMachineMode);
    }

    [Fact]
    public void InfoShouldRenderTables_DefaultInteractive_RendersTables()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState();

        var render = InfoCommand.ShouldRenderTables(null, shell, state);

        Assert.True(render);
        Assert.Equal(OutputFormat.User, state.OutputFormat);
    }

    [Fact]
    public void InfoShouldRenderTables_GlobalJson_DefersToPrintState()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = "json" };
        var state = new CommandState();

        var render = InfoCommand.ShouldRenderTables(null, shell, state);

        Assert.False(render);

        // No local --format was given, so info leaves the format unset and lets
        // PrintState apply the session default (honoring the global -o json).
        Assert.False(state.OutputFormatExplicitlySet);
    }

    [Fact]
    public void InfoShouldRenderTables_GlobalTable_RendersTables()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = "table" };
        var state = new CommandState();

        var render = InfoCommand.ShouldRenderTables(null, shell, state);

        Assert.True(render);
        Assert.Equal(OutputFormat.User, state.OutputFormat);
    }

    [Fact]
    public void InfoShouldRenderTables_GlobalCsv_DefersToPrintState()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = "csv" };
        var state = new CommandState();

        var render = InfoCommand.ShouldRenderTables(null, shell, state);

        Assert.False(render);
        Assert.False(state.OutputFormatExplicitlySet);
    }

    [Theory]
    [InlineData("json", OutputFormat.JSon)]
    [InlineData("csv", OutputFormat.CSV)]
    public void InfoShouldRenderTables_ExplicitStructuredFormat_YieldsToPrintState(string format, OutputFormat expected)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState();

        var render = InfoCommand.ShouldRenderTables(format, shell, state);

        Assert.False(render);
        Assert.Equal(expected, state.OutputFormat);
        Assert.True(state.OutputFormatExplicitlySet);
    }

    [Theory]
    [InlineData("table")]
    [InlineData("user")]
    public void InfoShouldRenderTables_ExplicitHumanFormat_RendersTables(string format)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState();

        var render = InfoCommand.ShouldRenderTables(format, shell, state);

        Assert.True(render);
        Assert.Equal(OutputFormat.User, state.OutputFormat);
    }

    [Fact]
    public void InfoShouldRenderTables_RedirectedTable_YieldsToPrintState()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.StdOutRedirect = "out.txt";
        var state = new CommandState();

        var render = InfoCommand.ShouldRenderTables("table", shell, state);

        Assert.False(render);
        Assert.Equal(OutputFormat.Table, state.OutputFormat);
    }

    [Fact]
    public void InfoShouldRenderTables_InvalidFormat_Throws()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var state = new CommandState();

        Assert.Throws<CommandException>(() => InfoCommand.ShouldRenderTables("xml", shell, state));
    }
}
