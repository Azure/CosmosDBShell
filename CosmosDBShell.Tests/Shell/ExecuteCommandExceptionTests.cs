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
    public async Task ExecuteCommandAsync_UnclosedBrace_ReturnsParserErrorState()
    {
        using var interpreter = CreateInterpreter();

        var state = await interpreter.ExecuteCommandAsync("{ { ls   }", CancellationToken.None);

        Assert.True(state.IsError);
        var parserState = Assert.IsType<ParserErrorCommandState>(state);
        Assert.NotEmpty(parserState.Errors);
        Assert.Contains(parserState.Errors, e => e.ErrorLevel == Azure.Data.Cosmos.Shell.Parser.ErrorLevel.Error);
    }

    [Fact]
    public async Task ExecuteCommandAsync_ParserError_WritesLocationToErrRedirect()
    {
        using var interpreter = CreateInterpreter();
        var tempFile = Path.GetTempFileName();
        try
        {
            interpreter.ErrOutRedirect = tempFile;

            var state = await interpreter.ExecuteCommandAsync("{ { ls   }", CancellationToken.None);

            Assert.True(state.IsError);
            Assert.IsType<ParserErrorCommandState>(state);
            var content = File.ReadAllText(tempFile);
            Assert.Contains("parse error:", content, StringComparison.Ordinal);
            Assert.Contains("(1:", content, StringComparison.Ordinal);
            Assert.Contains("{ { ls   }", content, StringComparison.Ordinal);
            Assert.Contains("^", content, StringComparison.Ordinal);
        }
        finally
        {
            interpreter.ErrOutRedirect = null;
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteCommandAsync_ParserError_ReportsOnlyFirstError()
    {
        using var interpreter = CreateInterpreter();
        var tempFile = Path.GetTempFileName();
        try
        {
            interpreter.ErrOutRedirect = tempFile;

            var state = await interpreter.ExecuteCommandAsync("{ { ls   }", CancellationToken.None);

            Assert.True(state.IsError);
            var parserState = Assert.IsType<ParserErrorCommandState>(state);
            Assert.NotEmpty(parserState.Errors);

            var content = File.ReadAllText(tempFile);
            var occurrences = content.Split("parse error:", StringSplitOptions.None).Length - 1;
            Assert.Equal(1, occurrences);
        }
        finally
        {
            interpreter.ErrOutRedirect = null;
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteCommandAsync_ParserError_ExpandsTabsForCaretAlignment()
    {
        using var interpreter = CreateInterpreter();
        var tempFile = Path.GetTempFileName();
        try
        {
            interpreter.ErrOutRedirect = tempFile;

            // Leading tab followed by an unexpected '}'. With tabSize=4 the
            // brace renders at visual column 5; the caret must land directly
            // under it on the displayed source line.
            var state = await interpreter.ExecuteCommandAsync("\t}", CancellationToken.None);

            Assert.True(state.IsError);
            Assert.IsType<ParserErrorCommandState>(state);

            var content = File.ReadAllText(tempFile);
            var lines = content.Replace("\r\n", "\n").Split('\n');

            var sourceLine = Array.Find(lines, l => l.Contains("> 1 |"));
            var caretLine = Array.Find(lines, l => l.Contains('^') && !l.Contains("parse "));

            Assert.NotNull(sourceLine);
            Assert.NotNull(caretLine);

            // Tab must be expanded — no raw tab in the echoed source line.
            Assert.DoesNotContain('\t', sourceLine!);

            // Caret column on the caret line must point at the '}' on the
            // displayed source line.
            var caretIndex = caretLine!.IndexOf('^');
            Assert.True(caretIndex >= 0);
            Assert.True(sourceLine!.Length > caretIndex);
            Assert.Equal('}', sourceLine[caretIndex]);
        }
        finally
        {
            interpreter.ErrOutRedirect = null;
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void TryReportQueryError_StructuredCosmosMessage_WritesCompilerStyleDiagnostic()
    {
        using var interpreter = CreateInterpreter();
        var tempFile = Path.GetTempFileName();
        try
        {
            interpreter.ErrOutRedirect = tempFile;

            const string query = "SELECT * FORM c";
            const string message = "Message: {\"errors\":[{\"severity\":\"Error\",\"location\":{\"start\":9,\"end\":13},\"message\":\"Identifier 'FORM' could not be resolved.\"}]}";

            var reported = interpreter.TryReportQueryError(query, message);

            Assert.True(reported);
            var content = File.ReadAllText(tempFile);
            Assert.Contains("query error:", content, StringComparison.Ordinal);
            Assert.Contains("Identifier 'FORM' could not be resolved.", content, StringComparison.Ordinal);
            Assert.Contains("(1:10)", content, StringComparison.Ordinal);
            Assert.Contains("> 1 | SELECT * FORM c", content, StringComparison.Ordinal);
            Assert.Contains("^^^^", content, StringComparison.Ordinal);
        }
        finally
        {
            interpreter.ErrOutRedirect = null;
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void TryReportQueryError_UnknownShape_ReturnsFalseAndWritesNothing()
    {
        using var interpreter = CreateInterpreter();
        var tempFile = Path.GetTempFileName();
        try
        {
            interpreter.ErrOutRedirect = tempFile;

            var reported = interpreter.TryReportQueryError(
                "SELECT * FROM c",
                "Throttled: too many requests for partition X.");

            Assert.False(reported);
            Assert.Empty(File.ReadAllText(tempFile));
        }
        finally
        {
            interpreter.ErrOutRedirect = null;
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParserError_RedirectedFile_DoesNotContainSpectreMarkup()
    {
        using var interpreter = CreateInterpreter();
        var tempFile = Path.GetTempFileName();
        try
        {
            interpreter.ErrOutRedirect = tempFile;

            await interpreter.ExecuteCommandAsync("\t}", CancellationToken.None);

            var content = File.ReadAllText(tempFile);
            Assert.DoesNotContain("[red]", content, StringComparison.Ordinal);
            Assert.DoesNotContain("[grey]", content, StringComparison.Ordinal);
            Assert.DoesNotContain("[/]", content, StringComparison.Ordinal);
        }
        finally
        {
            interpreter.ErrOutRedirect = null;
            File.Delete(tempFile);
        }
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
