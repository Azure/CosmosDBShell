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
    public void CommandException_CosmosTimeoutCancellation_UsesFriendlyMessage()
    {
        var exception = new OperationCanceledException(
            "The operation was canceled." + Environment.NewLine +
            "Cancellation Token has expired: True. Learn more at: https://aka.ms/cosmosdb-tsg-request-timeout" + Environment.NewLine +
            "CosmosDiagnostics: {\"Summary\":{\"DirectCalls\":{\"(410, 20001)\":3}}}");

        var commandException = new CommandException("query", exception);

        Assert.Contains("request timed out", commandException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--verbose", commandException.Message);
        Assert.DoesNotContain("CosmosDiagnostics", commandException.Message);
        Assert.DoesNotContain("Cancellation Token has expired", commandException.Message);
    }

    [Fact]
    public void CommandException_NestedCosmosTimeoutCancellation_UsesFriendlyMessage()
    {
        var timeout = new OperationCanceledException("A client transport error occurred: The request timed out while waiting for a server response. ReceiveTimeout");
        var exception = new InvalidOperationException("Outer failure", timeout);

        var commandException = new CommandException("connect", exception);

        Assert.Contains("request timed out", commandException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReceiveTimeout", commandException.Message);
        Assert.DoesNotContain("Outer failure", commandException.Message);
    }

    [Fact]
    public void CommandException_HttpTimeoutStatus_UsesFriendlyMessage()
    {
        var message = CommandException.GetDisplayMessage(System.Net.HttpStatusCode.RequestTimeout, "raw timeout body");

        Assert.Contains("request timed out", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw timeout body", message);
    }

    [Fact]
    public void CommandException_ResponseStatusTimeout_PreservesRawMessageForVerboseDiagnostics()
    {
        var exception = CommandException.FromResponseStatus(
            "query",
            System.Net.HttpStatusCode.RequestTimeout,
            "raw timeout body with CosmosDiagnostics");

        Assert.Contains("request timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CosmosDiagnostics", exception.Message);
        Assert.NotNull(exception.InnerException);
        Assert.Equal("raw timeout body with CosmosDiagnostics", exception.InnerException.Message);
        Assert.Contains("CosmosDiagnostics", exception.ToString());
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
