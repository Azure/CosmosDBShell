// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

using Azure.Data.Cosmos.Shell.Core;

public class ExecuteCommandExceptionTests
{
    private ShellInterpreter CreateInterpreter()
    {
        return new ShellInterpreter();
    }

    [Fact]
    public async Task ExecuteCommandAsync_ShellException_ReturnsErrorState()
    {
        using var interpreter = CreateInterpreter();

        // Accessing an undefined variable throws ShellException
        var state = await interpreter.ExecuteCommandAsync("echo $undefined_var_xyz", CancellationToken.None);

        Assert.True(state.IsError);
        var errorState = Assert.IsType<ErrorCommandState>(state);
        Assert.IsType<ShellException>(errorState.Exception);
    }

    [Fact]
    public async Task ExecuteCommandAsync_CommandException_ReturnsErrorState()
    {
        using var interpreter = CreateInterpreter();

        // ftab without piped input throws CommandException
        var state = await interpreter.ExecuteCommandAsync("ftab", CancellationToken.None);

        Assert.True(state.IsError);
        var errorState = Assert.IsType<ErrorCommandState>(state);
        Assert.IsType<CommandException>(errorState.Exception);
    }

    [Fact]
    public async Task ExecuteCommandAsync_TaskCanceled_ReturnsNonErrorState()
    {
        using var interpreter = CreateInterpreter();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var state = await interpreter.ExecuteCommandAsync("help", cts.Token);

        Assert.False(state.IsError);
    }

    [Fact]
    public async Task ExecuteCommandAsync_ShellException_PreservesExceptionInErrorState()
    {
        using var interpreter = CreateInterpreter();

        var state = await interpreter.ExecuteCommandAsync("echo $no_such_variable", CancellationToken.None);

        var errorState = Assert.IsType<ErrorCommandState>(state);
        Assert.Contains("no_such_variable", errorState.Exception.Message);
    }

    [Fact]
    public async Task ExecuteCommandAsync_CommandException_PreservesExceptionInErrorState()
    {
        using var interpreter = CreateInterpreter();

        var state = await interpreter.ExecuteCommandAsync("ftab", CancellationToken.None);

        var errorState = Assert.IsType<ErrorCommandState>(state);
        Assert.IsType<CommandException>(errorState.Exception);
        Assert.Equal("ftab", ((CommandException)errorState.Exception).Command);
    }

    [Fact]
    public async Task ExecuteCommandAsync_ErrorRedirect_WritesErrorToFile()
    {
        using var interpreter = CreateInterpreter();
        var tempFile = Path.GetTempFileName();
        try
        {
            interpreter.ErrOutRedirect = tempFile;

            var state = await interpreter.ExecuteCommandAsync("echo $redirect_error_test", CancellationToken.None);

            Assert.True(state.IsError);
            var content = File.ReadAllText(tempFile);
            Assert.Contains("redirect_error_test", content);
        }
        finally
        {
            interpreter.ErrOutRedirect = null;
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteCommandAsync_VerboseMode_ReturnsErrorState()
    {
        using var interpreter = CreateInterpreter();
        interpreter.Options = new Program.CosmosShellOptions { Verbose = true };

        var state = await interpreter.ExecuteCommandAsync("echo $verbose_test_var", CancellationToken.None);

        Assert.True(state.IsError);
        Assert.IsType<ErrorCommandState>(state);
    }

    [Fact]
    public async Task ExecuteCommandAsync_SuccessfulCommand_ReturnsNonErrorState()
    {
        using var interpreter = CreateInterpreter();

        var state = await interpreter.ExecuteCommandAsync("help", CancellationToken.None);

        Assert.False(state.IsError);
    }
}
