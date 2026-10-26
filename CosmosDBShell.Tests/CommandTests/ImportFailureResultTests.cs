// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;
using NSubstitute;

[Collection(ConsoleOutputTestCollection.Name)]
public class ImportFailureResultTests
{
    [Theory]
    [InlineData(429, ShellExitCode.Throttled, false, false)]
    [InlineData(429, ShellExitCode.Throttled, true, false)]
    [InlineData(429, ShellExitCode.Throttled, true, true)]
    [InlineData(401, ShellExitCode.AuthFailure, true, false)]
    [InlineData(403, ShellExitCode.AuthFailure, true, false)]
    [InlineData(404, ShellExitCode.NotFound, true, false)]
    [InlineData(503, ShellExitCode.ConnectionError, true, false)]
    [InlineData(409, ShellExitCode.GeneralFailure, true, false)]
    public async Task WriteFailure_PreservesPartialResultClassificationAndCharge(int status, int exitCode, bool continueOnError, bool streamFailure)
    {
        var file = Path.GetTempFileName();
        var stdout = Path.GetTempFileName();
        var stderr = Path.GetTempFileName();
        using var shell = CreateShell(out var container);
        shell.Options = new Program.CosmosShellOptions { Output = "json", Quiet = true };
        shell.StdOutRedirect = stdout;
        shell.ErrOutRedirect = stderr;
        using var streamFailureResponse = new ResponseMessage((HttpStatusCode)status);
        streamFailureResponse.Headers.Add("x-ms-request-charge", "3.5");
        container.CreateItemStreamAsync(Arg.Any<Stream>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                using var item = JsonDocument.Parse(call.ArgAt<Stream>(0));
                if (streamFailure && item.RootElement.GetProperty("id").GetString() == "bad")
                {
                    return Task.FromResult(streamFailureResponse);
                }

                return item.RootElement.GetProperty("id").GetString() == "bad"
                    ? Task.FromException<ResponseMessage>(new CosmosException("write failed", (HttpStatusCode)status, 0, "test", 3.5))
                    : Task.FromResult(Response(2));
            });
        try
        {
            await File.WriteAllTextAsync(file, """[{"id":"first","pk":"x"},{"id":"bad","pk":"x"},{"id":"last","pk":"x"}]""", TestContext.Current.CancellationToken);
            var script = $"import {JsonSerializer.Serialize(file)} --concurrency=1" + (continueOnError ? " --continue-on-error" : string.Empty);

            var state = Assert.IsType<StructuredErrorCommandState>(await shell.ExecuteCommandAsync(script, TestContext.Current.CancellationToken));

            var imported = continueOnError ? 2 : 1;
            var charge = (imported * 2.0) + 3.5;
            var result = Assert.IsType<ShellJson>(state.Result).Value;
            Assert.Equal(imported, result.GetProperty("imported").GetInt32());
            Assert.Equal(1, result.GetProperty("failed").GetInt32());
            Assert.Equal(charge, result.GetProperty("requestCharge").GetDouble());
            Assert.Equal(exitCode, result.GetProperty("exitCode").GetInt32());
            Assert.Equal(exitCode, state.ExitCode);
            Assert.Equal(charge + 1.25, state.RequestCharge);
            Assert.Equal(charge + 1.25, shell.SessionRequestCharge);
            Assert.Contains("Line 2", result.GetProperty("error").GetString());
            Assert.Empty(await File.ReadAllTextAsync(stdout, TestContext.Current.CancellationToken));
            using var error = JsonDocument.Parse(await File.ReadAllTextAsync(stderr, TestContext.Current.CancellationToken));
            Assert.Equal("error", error.RootElement.GetProperty("status").GetString());
            Assert.True(JsonElement.DeepEquals(result, error.RootElement.GetProperty("result")));

            var mcp = McpResponseFactory.CreateSuccess(state, shell.State);
            Assert.True(mcp.IsError);
            var payload = Assert.IsType<JsonElement>(mcp.StructuredContent);
            Assert.True(JsonElement.DeepEquals(result, payload.GetProperty("result")));
            Assert.Equal(charge + 1.25, payload.GetProperty("requestCharge").GetDouble());
        }
        finally
        {
            shell.StdOutRedirect = null;
            shell.ErrOutRedirect = null;
            File.Delete(file);
            File.Delete(stdout);
            File.Delete(stderr);
        }
    }

    [Fact]
    public async Task InputFailure_WaitsForStartedWriteAndRetainsItsResult()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var file = Path.GetTempFileName();
        using var shell = CreateShell(out var container);
        var write = new TaskCompletionSource<ResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        container.CreateItemStreamAsync(Arg.Any<Stream>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.TrySetResult();
                return write.Task;
            });
        try
        {
            await File.WriteAllTextAsync(file, """[{"id":"first","pk":"x"},42]""", timeout.Token);
            var command = new ImportCommand { File = file, Concurrency = 2, ContinueOnError = true };

            var import = command.ExecuteAsync(shell, new CommandState(), "import", timeout.Token);
            await started.Task.WaitAsync(timeout.Token);
            Assert.Single(container.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(Container.CreateItemStreamAsync));
            Assert.False(import.IsCompleted);
            write.SetResult(Response(2));
            var state = Assert.IsType<StructuredErrorCommandState>(await import.WaitAsync(timeout.Token));

            var result = Assert.IsType<ShellJson>(state.Result).Value;
            Assert.Equal(1, result.GetProperty("imported").GetInt32());
            Assert.Equal(0, result.GetProperty("failed").GetInt32());
            Assert.Equal(2, result.GetProperty("requestCharge").GetDouble());
            Assert.Contains("object", result.GetProperty("error").GetString());
            Assert.True(state.IsError);
        }
        finally
        {
            if (!write.Task.IsCompleted)
            {
                write.TrySetResult(Response(2));
            }
            File.Delete(file);
        }
    }

    [Fact]
    public async Task AllWritesFail_ResultRetainsChargeAndFirstObservedFailure()
    {
        var file = Path.GetTempFileName();
        using var shell = CreateShell(out var container);
        var calls = 0;
        container.CreateItemStreamAsync(Arg.Any<Stream>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<ResponseMessage>(
                new CosmosException("write failed", ++calls == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.Forbidden, 0, "test", 3)));
        try
        {
            await File.WriteAllTextAsync(file, """[{"id":"first","pk":"x"},{"id":"last","pk":"x"}]""", TestContext.Current.CancellationToken);

            var command = new ImportCommand { File = file, Concurrency = 1, ContinueOnError = true };
            var state = Assert.IsType<StructuredErrorCommandState>(
                await command.ExecuteAsync(shell, new CommandState(), "import", TestContext.Current.CancellationToken));

            var result = Assert.IsType<ShellJson>(state.Result).Value;
            Assert.Equal(0, result.GetProperty("imported").GetInt32());
            Assert.Equal(2, result.GetProperty("failed").GetInt32());
            Assert.Equal(6, result.GetProperty("requestCharge").GetDouble());
            Assert.Equal(ShellExitCode.Throttled, state.ExitCode);
            Assert.Contains("Line 1", result.GetProperty("error").GetString());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task DryRunFailure_ReturnsStructuredParseErrorWithoutWrites()
    {
        var file = Path.GetTempFileName();
        using var shell = ShellInterpreter.CreateInstance();
        try
        {
            await File.WriteAllTextAsync(file, """{"id":"first"} garbage""", TestContext.Current.CancellationToken);

            var state = Assert.IsType<StructuredErrorCommandState>(
                await new ImportCommand { File = file, DryRun = true }.ExecuteAsync(shell, new CommandState(), "import", TestContext.Current.CancellationToken));

            var result = Assert.IsType<ShellJson>(state.Result).Value;
            Assert.True(result.GetProperty("dryRun").GetBoolean());
            Assert.Equal(0, result.GetProperty("failed").GetInt32());
            Assert.Equal(0, result.GetProperty("requestCharge").GetDouble());
            Assert.True(state.IsError);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DryRunFailure_UserMode_HonorsErrorRedirection(bool append)
    {
        var file = Path.GetTempFileName();
        var stderr = Path.GetTempFileName();
        var originalOut = Console.Out;
        var originalConsole = Spectre.Console.AnsiConsole.Console;
        using var output = new StringWriter();
        using var shell = ShellInterpreter.CreateInstance();
        try
        {
            await File.WriteAllTextAsync(file, """{"id":"first"} garbage""", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(stderr, "previous" + Environment.NewLine, TestContext.Current.CancellationToken);
            Console.SetOut(output);
            Spectre.Console.AnsiConsole.Console = Spectre.Console.AnsiConsole.Create(new Spectre.Console.AnsiConsoleSettings
            {
                Ansi = Spectre.Console.AnsiSupport.No,
                ColorSystem = Spectre.Console.ColorSystemSupport.NoColors,
                Out = new Spectre.Console.AnsiConsoleOutput(output),
            });

            var script = $"import {JsonSerializer.Serialize(file)} --dry-run {(append ? "2>>" : "2>")}{JsonSerializer.Serialize(stderr)}";
            var state = Assert.IsType<StructuredErrorCommandState>(await shell.ExecuteCommandAsync(script, TestContext.Current.CancellationToken));

            Assert.True(Assert.IsType<ShellJson>(state.Result).Value.GetProperty("dryRun").GetBoolean());
            var errorText = await File.ReadAllTextAsync(stderr, TestContext.Current.CancellationToken);
            Assert.Contains(state.Exception.Message, errorText);
            Assert.Equal(append, errorText.StartsWith("previous", StringComparison.Ordinal));
            var text = output.ToString();
            Assert.Contains(MessageService.GetArgsString("command-import-dry-run-stopped", "count", 1), text);
            Assert.DoesNotContain("Line 1", text);
        }
        finally
        {
            Console.SetOut(originalOut);
            Spectre.Console.AnsiConsole.Console = originalConsole;
            shell.ErrOutRedirect = null;
            File.Delete(file);
            File.Delete(stderr);
        }
    }

    [Theory]
    [InlineData("\\uD800", "csv")]
    [InlineData("\\uD800", "json")]
    [InlineData("\\uDC00", "user")]
    [InlineData("\\uDC00", "table")]
    public async Task DryRunFailure_InvalidUnicodeId_PreservesValidationCountAndFormat(string escapedId, string format)
    {
        var file = Path.GetTempFileName();
        using var shell = ShellInterpreter.CreateInstance();
        try
        {
            await File.WriteAllTextAsync(
                file,
                """{"id":"first"}""" + "\n{\"id\":\"" + escapedId + "\"}",
                TestContext.Current.CancellationToken);
            var command = new ImportCommand { File = file, DryRun = true, Format = format };
            var selectedFormat = new CommandState();
            selectedFormat.SetFormat(format);

            var state = Assert.IsType<StructuredErrorCommandState>(
                await command.ExecuteAsync(shell, new CommandState(), "import", TestContext.Current.CancellationToken));

            var result = Assert.IsType<ShellJson>(state.Result).Value;
            Assert.Equal(1, result.GetProperty("validated").GetInt32());
            Assert.Equal(0, result.GetProperty("imported").GetInt32());
            Assert.Equal(0, result.GetProperty("requestCharge").GetDouble());
            Assert.True(result.GetProperty("dryRun").GetBoolean());
            Assert.Equal(ShellExitCode.UsageError, result.GetProperty("exitCode").GetInt32());
            Assert.Equal(ShellExitCode.UsageError, state.ExitCode);
            Assert.Equal(selectedFormat.OutputFormat, state.OutputFormat);
            Assert.True(state.OutputFormatExplicitlySet);
            Assert.Equal(
                MessageService.GetArgsString("command-import-error-invalid_id", "line", 2),
                result.GetProperty("error").GetString());

            var mcp = McpResponseFactory.CreateSuccess(state, shell.State);
            Assert.True(mcp.IsError);
            var payload = Assert.IsType<JsonElement>(mcp.StructuredContent);
            Assert.True(JsonElement.DeepEquals(result, payload.GetProperty("result")));
            if (format == "csv")
            {
                Assert.Equal(state.GenerateOutputText(), payload.GetProperty("outputText").GetString());
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(1200, false)]
    [InlineData(1200, true)]
    [InlineData(1201, false)]
    [InlineData(1201, true)]
    [InlineData(12000, false)]
    [InlineData(12000, true)]
    [InlineData(12001, false)]
    [InlineData(12001, true)]
    public async Task DryRunFailure_MalformedBomUnicode_PreservesStructuredInputError(int codePage, bool afterValidItem)
    {
        var encoding = Encoding.GetEncoding(codePage);
        byte[] malformed = codePage switch
        {
            1200 => [0x00, 0xD8],
            1201 => [0xD8, 0x00],
            12000 => [0x00, 0xD8, 0x00, 0x00],
            12001 => [0x00, 0x00, 0xD8, 0x00],
            _ => throw new ArgumentOutOfRangeException(nameof(codePage)),
        };
        var prefix = afterValidItem ? """{"id":"first"}""" + "\n" + new string(' ', 40_000) : string.Empty;
        var file = Path.GetTempFileName();
        using var shell = ShellInterpreter.CreateInstance();
        try
        {
            await File.WriteAllBytesAsync(
                file,
                [.. encoding.GetPreamble(), .. encoding.GetBytes(prefix + "{\"id\":\""), .. malformed, .. encoding.GetBytes("\"}")],
                TestContext.Current.CancellationToken);

            var state = Assert.IsType<StructuredErrorCommandState>(
                await new ImportCommand { File = file, DryRun = true, Format = "csv" }
                    .ExecuteAsync(shell, new CommandState(), "import", TestContext.Current.CancellationToken));

            var result = Assert.IsType<ShellJson>(state.Result).Value;
            Assert.Equal(afterValidItem ? 1 : 0, result.GetProperty("validated").GetInt32());
            Assert.Equal(0, result.GetProperty("imported").GetInt32());
            Assert.Equal(0, result.GetProperty("requestCharge").GetDouble());
            Assert.Equal(ShellExitCode.UsageError, state.ExitCode);
            Assert.Equal(ShellExitCode.UsageError, result.GetProperty("exitCode").GetInt32());
            Assert.Equal(OutputFormat.CSV, state.OutputFormat);
            Assert.IsType<DecoderFallbackException>(state.Exception.InnerException?.InnerException);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task WriteFailure_MalformedBomUnicode_DoesNotWriteAlteredId()
    {
        var file = Path.GetTempFileName();
        using var shell = CreateShell(out var container);
        try
        {
            await File.WriteAllBytesAsync(
                file,
                [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("{\"id\":\""), 0x00, 0xD8, .. Encoding.Unicode.GetBytes("\"}")],
                TestContext.Current.CancellationToken);

            var state = Assert.IsType<StructuredErrorCommandState>(
                await new ImportCommand { File = file }.ExecuteAsync(shell, new CommandState(), "import", TestContext.Current.CancellationToken));

            Assert.Equal(ShellExitCode.UsageError, state.ExitCode);
            Assert.Equal(0, Assert.IsType<ShellJson>(state.Result).Value.GetProperty("imported").GetInt32());
            Assert.DoesNotContain(container.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(Container.CreateItemStreamAsync));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("array")]
    [InlineData("jsonl")]
    [InlineData("invalid")]
    public async Task InvalidFormat_DoesNotAccessContainerOrWriteItems(string format)
    {
        var file = Path.GetTempFileName();
        using var shell = CreateShell(out var container);
        container.ClearReceivedCalls();
        try
        {
            await File.WriteAllTextAsync(file, """[{"id":"first","pk":"x"}]""", TestContext.Current.CancellationToken);
            var command = new ImportCommand { File = file, Format = format };

            await Assert.ThrowsAsync<ArgumentException>(() =>
                command.ExecuteAsync(shell, new CommandState(), "import", TestContext.Current.CancellationToken));

            Assert.Empty(container.ReceivedCalls());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("{\"id\":\"first\",\"pk\":\"x\"}\n\n{\n\"id\":\"bad\",\"pk\":\"x\"\n}", false)]
    [InlineData("{\"id\":\"first\",\"pk\":\"x\"}\n\n{\n\"pk\":\"x\"\n}", true)]
    public async Task ObjectSequenceFailure_ReportsPhysicalStartLineAndPreservesFormat(string content, bool invalidId)
    {
        var file = Path.GetTempFileName();
        using var shell = CreateShell(out var container);
        container.CreateItemStreamAsync(Arg.Any<Stream>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                using var item = JsonDocument.Parse(call.ArgAt<Stream>(0));
                return item.RootElement.GetProperty("id").GetString() == "bad"
                    ? Task.FromException<ResponseMessage>(new CosmosException("write failed", HttpStatusCode.Conflict, 0, "test", 0))
                    : Task.FromResult(Response(2));
            });
        try
        {
            await File.WriteAllTextAsync(file, content, TestContext.Current.CancellationToken);
            var command = new ImportCommand { File = file, Format = "csv", Concurrency = 1, DryRun = invalidId };

            var state = Assert.IsType<StructuredErrorCommandState>(
                await command.ExecuteAsync(shell, new CommandState(), "import", TestContext.Current.CancellationToken));

            Assert.Equal(OutputFormat.CSV, state.OutputFormat);
            Assert.True(state.OutputFormatExplicitlySet);
            Assert.Contains("Line 3", Assert.IsType<ShellJson>(state.Result).Value.GetProperty("error").GetString());

            var mcp = McpResponseFactory.CreateSuccess(state, shell.State);
            Assert.True(mcp.IsError);
            var payload = Assert.IsType<JsonElement>(mcp.StructuredContent);
            Assert.True(JsonElement.DeepEquals(Assert.IsType<ShellJson>(state.Result).Value, payload.GetProperty("result")));
            Assert.Equal(state.Exception.Message, payload.GetProperty("error").GetString());
            Assert.Equal(state.GenerateOutputText(), payload.GetProperty("outputText").GetString());
            Assert.Contains("imported", payload.GetProperty("outputText").GetString());
            Assert.Equal(state.RequestCharge.HasValue, payload.TryGetProperty("requestCharge", out var requestCharge));
            if (state.RequestCharge.HasValue)
            {
                Assert.Equal(state.RequestCharge.Value, requestCharge.GetDouble());
            }
            Assert.Equal(
                payload.GetRawText(),
                Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(mcp.Content)).Text);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static ShellInterpreter CreateShell(out Container container)
    {
        var client = Substitute.For<CosmosClient>();
        client.Endpoint.Returns(new Uri("https://example.documents.azure.com"));
        var database = Substitute.For<Database>();
        container = Substitute.For<Container>();
        client.GetDatabase("db").Returns(database);
        database.GetContainer("items").Returns(container);
        var metadata = Substitute.For<ContainerResponse>();
        metadata.Resource.Returns(new ContainerProperties("items", "/pk"));
        metadata.RequestCharge.Returns(1.25);
        container.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>()).Returns(metadata);
        var shell = ShellInterpreter.CreateInstance();
        shell.State = new ContainerState("items", "db", client);
        return shell;
    }

    private static ResponseMessage Response(double charge)
    {
        var response = new ResponseMessage(HttpStatusCode.Created);
        response.Headers.Add("x-ms-request-charge", charge.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }
}
