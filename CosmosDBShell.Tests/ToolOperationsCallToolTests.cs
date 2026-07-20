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
using Azure.Data.Cosmos.Shell.Mcp;

using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Spectre.Console;

// Exercises ToolOperations.CallToolHandler / ListToolsHandler against the shared
// ShellInterpreter.Instance singleton. Placed in the theme-state collection so the
// success path (which writes the highlighted command line through AnsiConsole) does
// not race with other tests that swap the global console or theme.
[Collection(CosmosShell.Tests.Shell.ThemeStateTestCollection.Name)]
public class ToolOperationsCallToolTests
{
    private static ToolOperations CreateToolOperations()
    {
        return new ToolOperations(NullLogger<ToolOperations>.Instance);
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
    public async Task ConfirmDestructive_UserAccepts_ReturnsNull()
    {
        var tool = CreateToolOperations();

        var result = await tool.ConfirmDestructiveAsync(
            (_, _) => new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" }),
            "rmdb",
            "rmdb mydb",
            CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task ConfirmDestructive_UserDeclines_ReturnsErrorAndDoesNotExecute(string action)
    {
        var tool = CreateToolOperations();

        var result = await tool.ConfirmDestructiveAsync(
            (_, _) => new ValueTask<ElicitResult>(new ElicitResult { Action = action }),
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
            (_, _) => throw new InvalidOperationException("boom"),
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
            Assert.Contains("boom", error);
        }
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

    [Fact]
    public async Task CallTool_EchoCommand_ReturnsSuccessResult()
    {
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
}
