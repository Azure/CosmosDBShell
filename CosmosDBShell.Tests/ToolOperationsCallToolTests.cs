// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.States;

using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Spectre.Console;

// Exercises ToolOperations.CallToolHandler / ListToolsHandler against the shared
// ShellInterpreter.Instance singleton. Placed in the theme-state collection so the
// success path (which writes the highlighted command line through AnsiConsole) does
// not race with other tests that swap the global console or theme.
[Collection(CosmosShell.Tests.Shell.ThemeStateTestCollection.Name)]
public class ToolOperationsCallToolTests : IDisposable
{
    private readonly LocationResourceSubscriptions locationSubscriptions =
        new(NullLogger<LocationResourceSubscriptions>.Instance);

    public void Dispose()
    {
        this.locationSubscriptions.Dispose();
    }

    private ToolOperations CreateToolOperations()
    {
        return new ToolOperations(NullLogger<ToolOperations>.Instance, this.locationSubscriptions);
    }

    private static RequestContext<CallToolRequestParams> CallContext(string? name, Dictionary<string, JsonElement>? arguments = null)
    {
        var context = (RequestContext<CallToolRequestParams>)RuntimeHelpers.GetUninitializedObject(
            typeof(RequestContext<CallToolRequestParams>));
        if (name != null)
        {
            context.Params = new CallToolRequestParams { Name = name, Arguments = arguments };
        }

        return context;
    }

    private static RequestContext<ListToolsRequestParams> ListContext()
    {
        return (RequestContext<ListToolsRequestParams>)RuntimeHelpers.GetUninitializedObject(
            typeof(RequestContext<ListToolsRequestParams>));
    }

    private static JsonElement Json(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    private static (bool IsError, JsonElement Root, JsonDocument Document) ReadResult(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var document = JsonDocument.Parse(text);
        return (result.IsError == true, document.RootElement, document);
    }

    [Fact]
    public async Task CallTool_NullParams_ReturnsError()
    {
        var tool = CreateToolOperations();

        var result = await tool.CallToolHandler(CallContext(null), CancellationToken.None);

        var (isError, root, document) = ReadResult(result);
        using (document)
        {
            Assert.True(isError);
            Assert.Contains("null parameters", root.GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task CallTool_UnknownCommand_ReturnsError()
    {
        var tool = CreateToolOperations();

        var result = await tool.CallToolHandler(CallContext("definitely-not-a-command"), CancellationToken.None);

        var (isError, root, document) = ReadResult(result);
        using (document)
        {
            Assert.True(isError);
            Assert.Contains("Could not find command", root.GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task CallTool_RestrictedCommand_ReturnsError()
    {
        var tool = CreateToolOperations();
        Assert.True(ShellInterpreter.Instance.App.Commands["exit"].McpRestricted);

        var result = await tool.CallToolHandler(CallContext("exit"), CancellationToken.None);

        var (isError, root, document) = ReadResult(result);
        using (document)
        {
            Assert.True(isError);
            Assert.Contains("restricted for MCP", root.GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task ConfirmDestructive_NoElicitationSupport_RefusesAndDoesNotExecute()
    {
        var tool = CreateToolOperations();

        var result = await tool.ConfirmDestructiveAsync(
            null,
            "rmdb",
            "rmdb mydb",
            CancellationToken.None);

        Assert.NotNull(result);
        var (isError, root, document) = ReadResult(result!);
        using (document)
        {
            Assert.True(isError);
            var error = root.GetProperty("error").GetString();
            Assert.Contains("does not support confirmation prompts", error);
            Assert.Contains("rmdb mydb", error);
        }
    }

    [Fact]
    public async Task CallTool_RmWithPartitionKeyAndETag_ConfirmationShowsTargetConditions()
    {
        var tool = CreateToolOperations();
        var arguments = new Dictionary<string, JsonElement>
        {
            ["pattern"] = Json("\"order-123\""),
            ["key"] = Json("\"id\""),
            ["pk"] = Json("\"customer-42\""),
            ["etag"] = Json("\"\\\"etag-1\\\"\""),
            ["database"] = Json("\"MyDb\""),
            ["container"] = Json("\"Orders\""),
        };

        var result = await tool.CallToolHandler(CallContext("rm", arguments), CancellationToken.None);

        var (isError, root, document) = ReadResult(result);
        using (document)
        {
            Assert.True(isError);
            var error = root.GetProperty("error").GetString();
            Assert.Contains("does not support confirmation prompts", error);
            Assert.Contains("rm \"order-123\"", error);
            Assert.Contains("--partition-key \"customer-42\"", error);
            Assert.Contains("--etag \"\\\"etag-1\\\"\"", error);
        }
    }

    [Fact]
    public async Task CallTool_RmArgumentOrder_DoesNotChangeConfirmedCommandLine()
    {
        var tool = CreateToolOperations();
        var forward = new Dictionary<string, JsonElement>
        {
            ["pattern"] = Json("\"order-123\""),
            ["key"] = Json("\"id\""),
            ["partition-key"] = Json("\"customer-42\""),
            ["etag"] = Json("\"etag-1\""),
        };
        var reversed = forward.Reverse().ToDictionary();

        var errors = new List<string?>();
        foreach (var arguments in new[] { forward, reversed })
        {
            var (_, root, document) = ReadResult(await tool.CallToolHandler(CallContext("rm", arguments), CancellationToken.None));
            using (document)
            {
                errors.Add(root.GetProperty("error").GetString());
            }
        }

        Assert.Contains("rm \"order-123\" --key \"id\" --partition-key \"customer-42\" --etag \"etag-1\"", errors[0]);
        Assert.Equal(errors[0], errors[1]);
    }

    [Theory]
    [InlineData("etag", "command-rm-error-etag_empty")]
    [InlineData("partition-key", "command-rm-error-partition_key_missing_value")]
    public async Task CallTool_RmWithJsonNullSafetyOption_ConfirmedCommandFailsWithoutDeleting(string argumentName, string expectedKey)
    {
        var tool = CreateToolOperations();
        var factory = ShellInterpreter.Instance.App.Commands["rm"];
        var command = (RmCommand)factory.CreateCommand();
        var arguments = new Dictionary<string, JsonElement>
        {
            ["pattern"] = Json("\"order-123\""),
            ["key"] = Json("\"id\""),
            ["partition-key"] = Json("\"customer-42\""),
            [argumentName] = Json("null"),
        };

        foreach (var argument in arguments)
        {
            var option = factory.Options.FirstOrDefault(o => o.Name.Contains(argument.Key));
            var property = option?.PropertyInfo ?? factory.Parameters.First(p => p.Name.Contains(argument.Key)).PropertyInfo;
            property.SetValue(command, argument.Value.GetString());
        }

        var result = await tool.ExecuteToolAsync(
            factory,
            command,
            "rm \"order-123\" --key \"id\" --partition-key \"customer-42\"",
            (_, _, _) => new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" }),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(
            Azure.Data.Cosmos.Shell.Util.MessageService.GetString(expectedKey),
            Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task ConfirmDestructive_UserAccepts_ReturnsNull()
    {
        var tool = CreateToolOperations();

        var result = await tool.ConfirmDestructiveAsync(
            (_, _, _) => new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" }),
            "rmdb",
            "rmdb mydb",
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteTool_ContextChangesDuringConfirmation_RefusesExecution()
    {
        var shell = ShellInterpreter.Instance;
        var originalState = shell.State;
        var command = new TrackingCommand();
        try
        {
            var result = await CreateToolOperations().ExecuteToolAsync(
                shell.App.Commands["rm"], command, "rm test-*",
                (request, _, _) =>
                {
                    Assert.Contains("Account:", request.Message);
                    Assert.Contains("Current location:", request.Message);
                    shell.State = new DisconnectedState();
                    shell.State = originalState;
                    return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
                }, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.False(command.Executed);
            Assert.Contains("context changed", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        }
        finally
        {
            shell.State = originalState;
        }
    }

    private sealed class TrackingCommand : CosmosCommand
    {
        public bool Executed { get; private set; }

        public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
        {
            this.Executed = true;
            return Task.FromResult(commandState);
        }
    }

    [Fact]
    public async Task ExecuteTool_UnchangedContextAfterConfirmation_ExecutesCommand()
    {
        var command = new TrackingCommand();
        var result = await CreateToolOperations().ExecuteToolAsync(
            ShellInterpreter.Instance.App.Commands["rm"], command, "rm test-*",
            (_, _, _) => new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" }),
            TestContext.Current.CancellationToken);
        Assert.False(result.IsError == true);
        Assert.True(command.Executed);
    }

    [Fact]
    public async Task ExecuteTool_WithoutAnsiTerminal_EchoesPlainlyAndStillExecutes()
    {
        var command = new TrackingCommand();
        using var plain = new StringWriter();

        var savedConsole = AnsiConsole.Console;
        var savedOut = Console.Out;
        try
        {
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(plain),
            });
            Console.SetOut(plain);

            var result = await CreateToolOperations().ExecuteToolAsync(
                ShellInterpreter.Instance.App.Commands["rm"], command, "rm test-*",
                (_, _, _) => new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" }),
                TestContext.Current.CancellationToken);

            Assert.False(result.IsError == true);
            Assert.True(command.Executed);
        }
        finally
        {
            Console.SetOut(savedOut);
            AnsiConsole.Console = savedConsole;
        }

        Assert.Contains("rm test-*", plain.ToString(), StringComparison.Ordinal);
        Assert.Equal("rm test-*", ShellInterpreter.Instance.History.ToArray()[^1]);
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task ConfirmDestructive_UserDeclines_ReturnsErrorAndDoesNotExecute(string action)
    {
        var tool = CreateToolOperations();

        var result = await tool.ConfirmDestructiveAsync(
            (_, _, _) => new ValueTask<ElicitResult>(new ElicitResult { Action = action }),
            "rmdb",
            "rmdb mydb",
            CancellationToken.None);

        Assert.NotNull(result);
        var (isError, root, document) = ReadResult(result!);
        using (document)
        {
            Assert.True(isError);
            var error = root.GetProperty("error").GetString();
            Assert.Contains("was not approved by the user", error);
            Assert.Contains(action, error);
        }
    }

    [Fact]
    public async Task ConfirmDestructive_ElicitationThrows_ReturnsErrorAndDoesNotExecute()
    {
        var tool = CreateToolOperations();

        var result = await tool.ConfirmDestructiveAsync(
            (_, _, _) => throw new InvalidOperationException("boom"),
            "rmdb",
            "rmdb mydb",
            CancellationToken.None);

        Assert.NotNull(result);
        var (isError, root, document) = ReadResult(result!);
        using (document)
        {
            Assert.True(isError);
            var error = root.GetProperty("error").GetString();
            Assert.Contains("could not be completed", error);
            Assert.Contains("InvalidOperationException", error);
            Assert.DoesNotContain("boom", error);
        }
    }

    [Fact]
    public async Task CallTool_PositionalArgumentsOutOfOrder_DisplaysDeclarationOrder()
    {
        var tool = CreateToolOperations();
        var arguments = new Dictionary<string, JsonElement>
        {
            ["force"] = Json("true"),
            ["name"] = Json("\"OldDb\""),
        };

        var result = await tool.CallToolHandler(CallContext("rmdb", arguments), CancellationToken.None);

        var (isError, root, document) = ReadResult(result);
        using (document)
        {
            Assert.True(isError);
            Assert.Contains("rmdb \"OldDb\" \"True\"", root.GetProperty("error").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CallTool_PositionalGap_ReturnsErrorWithoutExecuting()
    {
        var tool = CreateToolOperations();
        var arguments = new Dictionary<string, JsonElement>
        {
            ["path"] = Json("\"dark.json\""),
        };

        var result = await tool.CallToolHandler(CallContext("theme", arguments), CancellationToken.None);

        var (isError, root, document) = ReadResult(result);
        using (document)
        {
            Assert.True(isError);
            var error = root.GetProperty("error").GetString();
            Assert.Contains("'path'", error, StringComparison.Ordinal);
            Assert.Contains("'action'", error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FindPositionalGap_DetectsOmittedAndNullPredecessors()
    {
        var parameters = ShellInterpreter.Instance.App.Commands["theme"].Parameters;

        Assert.NotNull(ToolOperations.FindPositionalGap(parameters, new Dictionary<Parameter, object?> { [parameters[2]] = "dark.json" }));
        Assert.NotNull(ToolOperations.FindPositionalGap(parameters, new Dictionary<Parameter, object?> { [parameters[0]] = null, [parameters[1]] = "dark" }));
        Assert.Null(ToolOperations.FindPositionalGap(parameters, new Dictionary<Parameter, object?> { [parameters[0]] = "show", [parameters[1]] = "dark" }));
    }

    [Fact]
    public void FormatPositionalsForHistory_RendersContiguousValuesAndExpandsArrays()
    {
        var parameters = ShellInterpreter.Instance.App.Commands["theme"].Parameters;
        var values = new Dictionary<Parameter, object?> { [parameters[0]] = "show", [parameters[1]] = "dark" };
        Assert.Equal(" \"show\" \"dark\"", ToolOperations.FormatPositionalsForHistory(parameters, values));

        var echoParameters = ShellInterpreter.Instance.App.Commands["echo"].Parameters;
        var echoValues = new Dictionary<Parameter, object?> { [echoParameters[0]] = new[] { "hello", "world" } };
        Assert.Equal(" \"hello\" \"world\"", ToolOperations.FormatPositionalsForHistory(echoParameters, echoValues));
    }

    [Fact]
    public async Task CallTool_UnknownArgument_ReturnsErrorListingKnownArguments()
    {
        var tool = CreateToolOperations();
        var arguments = new Dictionary<string, JsonElement>
        {
            ["bogus"] = Json("\"value\""),
        };

        var result = await tool.CallToolHandler(CallContext("echo", arguments), CancellationToken.None);

        var (isError, root, document) = ReadResult(result);
        using (document)
        {
            Assert.True(isError);
            var error = root.GetProperty("error").GetString();
            Assert.Contains("Unknown argument 'bogus'", error);
            Assert.Contains("Known arguments:", error);
        }
    }

    [Fact]
    public async Task CallTool_MissingRequiredParameter_ReturnsError()
    {
        var tool = CreateToolOperations();

        // 'query' requires the 'query' parameter; supplying none triggers the missing-required path.
        var result = await tool.CallToolHandler(CallContext("query"), CancellationToken.None);

        var (isError, root, document) = ReadResult(result);
        using (document)
        {
            Assert.True(isError);
            Assert.Contains("Missing required parameter", root.GetProperty("error").GetString());
        }
    }

    [Theory]
    [InlineData("query", "query")]
    [InlineData("batch", "subcommand")]
    public async Task CallTool_NullRequiredParameter_ReturnsMissingParameterError(string command, string parameter)
    {
        var tool = CreateToolOperations();
        var arguments = new Dictionary<string, JsonElement>
        {
            [parameter] = Json("null"),
        };

        var result = await tool.CallToolHandler(CallContext(command, arguments), CancellationToken.None);

        var (isError, root, document) = ReadResult(result);
        using (document)
        {
            Assert.True(isError);
            Assert.Contains("Missing required parameter", root.GetProperty("error").GetString());
        }
    }

    [Theory]
    [InlineData("begin")]
    [InlineData("add")]
    [InlineData("execute")]
    [InlineData("exec")]
    [InlineData("commit")]
    [InlineData("cancel")]
    [InlineData("abort")]
    [InlineData("status")]
    [InlineData("show")]
    public async Task CallTool_StatefulBatchSubcommand_ReturnsError(string subcommand)
    {
        var tool = CreateToolOperations();
        var arguments = new Dictionary<string, JsonElement>
        {
            ["subcommand"] = Json($"\"{subcommand}\""),
        };

        var result = await tool.CallToolHandler(CallContext("batch", arguments), CancellationToken.None);

        var (isError, root, document) = ReadResult(result);
        using (document)
        {
            Assert.True(isError);
            var error = root.GetProperty("error").GetString();
            Assert.Contains("only the stateless 'batch run'", error);
            Assert.Contains("manually in the shell", error);
        }
    }

    [Fact]
    public async Task CallTool_WhitespaceBatchSubcommand_ReturnsMissingSubcommandError()
    {
        var tool = CreateToolOperations();
        var arguments = new Dictionary<string, JsonElement>
        {
            ["subcommand"] = Json("\"   \""),
        };

        var result = await tool.CallToolHandler(CallContext("batch", arguments), CancellationToken.None);

        var (isError, root, document) = ReadResult(result);
        using (document)
        {
            Assert.True(isError);
            var error = root.GetProperty("error").GetString();
            Assert.Contains("subcommand", error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("only the stateless 'batch run'", error);
        }
    }

    [Fact]
    public async Task CallTool_InvalidValueType_ReturnsSanitizedError()
    {
        var tool = CreateToolOperations();
        var arguments = new Dictionary<string, JsonElement>
        {
            // 'max' is an integer option; a non-numeric string cannot convert.
            ["max"] = Json("\"not-a-number\""),
        };

        var result = await tool.CallToolHandler(CallContext("query", arguments), CancellationToken.None);

        var (isError, root, document) = ReadResult(result);
        using (document)
        {
            Assert.True(isError);
            var error = root.GetProperty("error").GetString();
            Assert.Contains("Invalid value for option '--max'", error);
            // The offending raw value must never be echoed back (secret-redaction contract).
            Assert.DoesNotContain("not-a-number", error);
        }
    }

    [Theory]
    [InlineData("42")]
    [InlineData("null")]
    public async Task CallTool_InvalidContinuationType_ReturnsError(string continuationJson)
    {
        var tool = CreateToolOperations();
        var arguments = new Dictionary<string, JsonElement>
        {
            ["query"] = Json("\"SELECT * FROM c\""),
            ["continuation"] = Json(continuationJson),
        };

        var result = await tool.CallToolHandler(CallContext("query", arguments), CancellationToken.None);

        var (isError, root, document) = ReadResult(result);
        using (document)
        {
            Assert.True(isError);
            Assert.Equal(
                "Invalid value for MCP argument 'continuation'. Expected a non-null string.",
                root.GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task CallTool_EchoCommand_ReturnsSuccessResult()
    {
        var tool = CreateToolOperations();
        var history = ShellInterpreter.Instance.History.ToArray();
        using var output = new StringWriter();
        var arguments = new Dictionary<string, JsonElement>
        {
            ["messages"] = Json("[\"hello\", \"world\"]"),
        };

        var saved = AnsiConsole.Console;
        try
        {
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(output),
            });

            var result = await tool.CallToolHandler(CallContext("echo", arguments), CancellationToken.None);

            Assert.Contains("echo", output.ToString(), StringComparison.Ordinal);
            var recorded = ShellInterpreter.Instance.History.ToArray();
            Assert.Equal(history.Length + 1, recorded.Length);
            Assert.Equal("echo \"hello\" \"world\"", recorded[^1]);
            Assert.Single(recorded, entry => entry == recorded[^1]);

            var (isError, root, document) = ReadResult(result);
            using (document)
            {
                Assert.False(isError);
                Assert.Equal("hello world", root.GetProperty("result").GetString());
                Assert.True(root.TryGetProperty("currentLocation", out _));
            }
        }
        finally
        {
            AnsiConsole.Console = saved;
        }
    }

    [Fact]
    public async Task CallTool_AfterSharedTokenSourceDisposedExternally_StillReturnsSuccessResult()
    {
        // Reproduces the startup `--connect` bug: Program.cs captures
        // ShellInterpreter.UserCancellationTokenSource in a `using`, disposing the shared
        // static token the instant the initial connect finished. Every MCP tool call
        // afterward hit ToolOperations.OnCallToolsAsync -> CancelPrompt() ->
        // currentTokenSource.Cancel(), which threw ObjectDisposedException on an object
        // still referenced by the static field. Simulate that exact precondition here
        // without touching Program.cs's process-level startup path.
        using (ShellInterpreter.UserCancellationTokenSource)
        {
            // Disposed on scope exit while still being the shared static instance.
        }

        var tool = CreateToolOperations();
        var arguments = new Dictionary<string, JsonElement>
        {
            ["messages"] = Json("[\"hello\", \"world\"]"),
        };

        var saved = AnsiConsole.Console;
        try
        {
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(new StringWriter()),
            });

            var result = await tool.CallToolHandler(CallContext("echo", arguments), CancellationToken.None);

            var (isError, root, document) = ReadResult(result);
            using (document)
            {
                Assert.False(isError);
                Assert.Equal("hello world", root.GetProperty("result").GetString());
            }
        }
        finally
        {
            AnsiConsole.Console = saved;
        }
    }

    [Fact]
    public async Task ListTools_ReturnsRegisteredTools()
    {
        var tool = CreateToolOperations();

        var result = await tool.ListToolsHandler(ListContext(), CancellationToken.None);

        Assert.NotEmpty(result.Tools);
        Assert.Contains(result.Tools, t => t.Name == "query");
        Assert.Contains(result.Tools, t => t.Name == "echo");
    }

    [Fact]
    public void ConfirmationRequestState_RoundTripsOnlyForTheSameCommand()
    {
        var state = ConfirmationRequestState.Create("rmdb mydb", 42);

        Assert.True(ConfirmationRequestState.TryRead(state, "rmdb mydb", out var version));
        Assert.Equal(42, version);
        Assert.False(ConfirmationRequestState.TryRead(state, "rmdb otherdb", out _));
    }

    [Fact]
    public void ConfirmationRequestState_CanBeReadOnlyOnce()
    {
        var state = ConfirmationRequestState.Create("rmdb mydb", 42);

        Assert.True(ConfirmationRequestState.TryRead(state, "rmdb mydb", out _));
        Assert.False(ConfirmationRequestState.TryRead(state, "rmdb mydb", out _));
    }

    [Fact]
    public void ConfirmationRequestState_ExpiresAfterLifetime()
    {
        var state = ConfirmationRequestState.Create("rmdb mydb", 42);

        Assert.False(ConfirmationRequestState.TryRead(
            state, "rmdb mydb", DateTimeOffset.UtcNow + ConfirmationRequestState.Lifetime + TimeSpan.FromSeconds(1), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-state")]
    [InlineData("Zm9v.YmFy")]
    public void ConfirmationRequestState_RejectsMissingOrForgedState(string? state)
    {
        Assert.False(ConfirmationRequestState.TryRead(state, "rmdb mydb", out _));
    }

    [Fact]
    public void ConfirmationRequestState_RejectsTamperedPayload()
    {
        var state = ConfirmationRequestState.Create("rmdb mydb", 1);
        var signature = state[(state.IndexOf('.') + 1)..];
        var forgedPayload = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            System.Text.Encoding.UTF8.GetBytes("1\nrmdb otherdb"));

        Assert.False(ConfirmationRequestState.TryRead(forgedPayload + "." + signature, "rmdb otherdb", out _));
    }

    [Fact]
    public async Task ExecuteTool_MrtrPrompt_RequestsConfirmationWithoutExecuting()
    {
        var command = new TrackingCommand();
        var shell = ShellInterpreter.Instance;

        var exception = await Assert.ThrowsAsync<InputRequiredException>(() => CreateToolOperations().ExecuteToolAsync(
            shell.App.Commands["rm"], command, "rm test-*",
            (request, stateVersion, _) => throw new InputRequiredException(
                inputRequests: new Dictionary<string, InputRequest> { ["confirm"] = InputRequest.ForElicitation(request) },
                requestState: ConfirmationRequestState.Create("rm test-*", stateVersion)),
            TestContext.Current.CancellationToken));

        Assert.False(command.Executed);
        var inputRequest = Assert.Single(exception.Result.InputRequests!);
        Assert.Equal("confirm", inputRequest.Key);
        Assert.Contains("rm test-*", inputRequest.Value.ElicitationParams!.Message);
        Assert.True(ConfirmationRequestState.TryRead(exception.Result.RequestState, "rm test-*", out var version));
        Assert.Equal(shell.StateVersion, version);
    }

    [Fact]
    public async Task ExecuteTool_AcceptedConfirmationResponse_ExecutesCommand()
    {
        var command = new TrackingCommand();
        var shell = ShellInterpreter.Instance;

        var result = await CreateToolOperations().ExecuteToolAsync(
            shell.App.Commands["rm"], command, "rm test-*", null,
            TestContext.Current.CancellationToken,
            ConfirmationRetry("accept", ConfirmationRequestState.Create("rm test-*", shell.StateVersion)));

        Assert.False(result.IsError == true);
        Assert.True(command.Executed);
    }

    [Fact]
    public async Task ExecuteTool_ReplayedConfirmationResponse_DoesNotExecuteAgain()
    {
        var shell = ShellInterpreter.Instance;
        var retry = ConfirmationRetry("accept", ConfirmationRequestState.Create("rm test-*", shell.StateVersion));
        var first = new TrackingCommand();
        var replayed = new TrackingCommand();

        await CreateToolOperations().ExecuteToolAsync(
            shell.App.Commands["rm"], first, "rm test-*", null, TestContext.Current.CancellationToken, retry);
        var result = await CreateToolOperations().ExecuteToolAsync(
            shell.App.Commands["rm"], replayed, "rm test-*", null, TestContext.Current.CancellationToken, retry);

        Assert.True(first.Executed);
        Assert.True(result.IsError);
        Assert.False(replayed.Executed);
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task ExecuteTool_DeclinedConfirmationResponse_DoesNotExecute(string action)
    {
        var command = new TrackingCommand();
        var shell = ShellInterpreter.Instance;

        var result = await CreateToolOperations().ExecuteToolAsync(
            shell.App.Commands["rm"], command, "rm test-*", null,
            TestContext.Current.CancellationToken,
            ConfirmationRetry(action, ConfirmationRequestState.Create("rm test-*", shell.StateVersion)));

        Assert.True(result.IsError);
        Assert.False(command.Executed);
        Assert.Contains("was not approved by the user", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task ExecuteTool_ConfirmationResponseForOtherCommand_DoesNotExecute()
    {
        var command = new TrackingCommand();
        var shell = ShellInterpreter.Instance;

        var result = await CreateToolOperations().ExecuteToolAsync(
            shell.App.Commands["rm"], command, "rm *", null,
            TestContext.Current.CancellationToken,
            ConfirmationRetry("accept", ConfirmationRequestState.Create("rm test-*", shell.StateVersion)));

        Assert.True(result.IsError);
        Assert.False(command.Executed);
        Assert.Contains("does not match this command", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task ExecuteTool_ContextChangedBetweenConfirmationRounds_DoesNotExecute()
    {
        var shell = ShellInterpreter.Instance;
        var originalState = shell.State;
        var command = new TrackingCommand();
        var requestState = ConfirmationRequestState.Create("rm test-*", shell.StateVersion);
        try
        {
            shell.State = new DisconnectedState();
            shell.State = originalState;

            var result = await CreateToolOperations().ExecuteToolAsync(
                shell.App.Commands["rm"], command, "rm test-*", null,
                TestContext.Current.CancellationToken,
                ConfirmationRetry("accept", requestState));

            Assert.True(result.IsError);
            Assert.False(command.Executed);
            Assert.Contains("context changed", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        }
        finally
        {
            shell.State = originalState;
        }
    }

    private static CallToolRequestParams ConfirmationRetry(string action, string requestState)
    {
        return new CallToolRequestParams
        {
            Name = "rm",
            RequestState = requestState,
            InputResponses = new Dictionary<string, InputResponse>
            {
                ["confirm"] = InputResponse.FromElicitResult(new ElicitResult { Action = action }),
            },
        };
    }
}
