// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;
using NSubstitute;
using DataType = Azure.Data.Cosmos.Shell.Parser.DataType;

[Collection(ConsoleOutputTestCollection.Name)]
public class PipelineInputTests
{
    [Theory]
    [InlineData("echo [1]; filter '.'")]
    [InlineData("{ echo [1]; filter '.' }")]
    [InlineData("echo [1]; exec filter '.'")]
    [InlineData("echo [1]; $value = (filter '.')")]
    [InlineData("def consume [] { filter '.' }; echo [1]; consume")]
    [InlineData("echo [1]; if (true) { filter '.' }")]
    [InlineData("for $i in [1,2] { if ($i == 2) { filter '.' }; echo [1] }")]
    [InlineData("$i = 0; while ($i < 2) { $i = $i + 1; if ($i == 2) { filter '.' }; echo [1] }")]
    [InlineData("def consume [] { filter '.'; filter '.' }; echo [1] | consume")]
    [InlineData("echo [1] | filter '.'; filter '.'")]
    [InlineData("def make { return [2] }; def consume { make; filter '.' }; echo [1] | consume")]
    [InlineData("def make { return $. }; def consume { make; filter '.' }; echo [1] | consume")]
    [InlineData("def make { }; def consume { make; filter '.' }; echo [1] | consume")]
    public async Task SequentialCommands_DoNotReceiveImplicitInput(string script)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var exception = await Record.ExceptionAsync(
            () => shell.RunCommandAsync(new CommandState(), script, TestContext.Current.CancellationToken));

        Assert.NotNull(exception);
        Assert.Contains("requires piped JSON input", exception.ToString());
    }

    [Theory]
    [InlineData("echo [1] | filter '.'")]
    [InlineData("echo [9]; echo [1] | filter '.'")]
    [InlineData("echo [1] | exec filter '.'")]
    [InlineData("def consume [] { filter '.' }; echo [1] | consume")]
    [InlineData("echo [1] | filter (echo '.')")]
    [InlineData("echo '{\"path\":\".items\",\"items\":[1]}' | filter (echo $.path)")]
    [InlineData("def path [value] { return $value }; echo '{\"path\":\".items\",\"items\":[1]}' | filter (path $.path)")]
    [InlineData("def make { return [1] }; def consume { make | filter '.' }; echo [2] | consume")]
    [InlineData("def passthrough { return $. }; echo [1] | passthrough | filter '.'")]
    public async Task ExplicitPipelines_PreserveTheirInput(string script)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var state = await shell.RunCommandAsync(new CommandState(), script, TestContext.Current.CancellationToken);

        Assert.False(state.IsError);
        var json = Assert.IsType<JsonElement>(state.Result?.ConvertShellObject(DataType.Json));
        Assert.Equal(1, Assert.Single(json.EnumerateArray()).GetInt32());
        Assert.False(state.HasPipelineInput);
    }

    [Theory]
    [InlineData("echo stale; echo")]
    [InlineData("echo stale; $value = (echo); echo $value")]
    public async Task CommandsAndExpressions_DoNotReplayPreviousOutput(string script)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var state = await shell.RunCommandAsync(new CommandState(), script, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, state.Result?.ConvertShellObject(DataType.Text));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ScriptInput_RequiresPipeAndIsConsumedByFirstCommand(bool piped, bool secondCommand)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var path = Path.Combine(Path.GetTempPath(), $"cosmosshell-pipeline-{Guid.NewGuid():N}.csh");
        try
        {
            await File.WriteAllTextAsync(path, secondCommand ? "filter '.'; filter '.'" : "filter '.'", TestContext.Current.CancellationToken);
            var script = $"echo [1] {(piped ? "|" : ";")} exec {ShellLiteral.Quote(path)}";
            if (piped && !secondCommand)
            {
                var state = await shell.RunCommandAsync(new CommandState(), script, TestContext.Current.CancellationToken);
                Assert.False(state.IsError);
                Assert.Equal("[1]", state.Result?.ConvertShellObject(DataType.Json)?.ToString());
            }
            else
            {
                var exception = await Record.ExceptionAsync(
                    () => shell.RunCommandAsync(new CommandState(), script, TestContext.Current.CancellationToken));
                Assert.NotNull(exception);
                Assert.Contains("requires piped JSON input", exception.ToString());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NestedScript_ReturnedResultRequiresExplicitPipe(bool outerScript, bool explicitPipe)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var path = Path.Combine(Path.GetTempPath(), $"cosmosshell-nested-{Guid.NewGuid():N}.csh");
        var outerPath = path + ".outer.csh";
        try
        {
            await File.WriteAllTextAsync(path, "return $.", TestContext.Current.CancellationToken);
            var body = $"exec {ShellLiteral.Quote(path)} {(explicitPipe ? "|" : ";")} filter '.'";
            string script;
            if (outerScript)
            {
                await File.WriteAllTextAsync(outerPath, body, TestContext.Current.CancellationToken);
                script = $"echo [1] | exec {ShellLiteral.Quote(outerPath)}";
            }
            else
            {
                script = $"def consume {{ {body} }}; echo [1] | consume";
            }

            if (explicitPipe)
            {
                var state = await shell.RunCommandAsync(new CommandState(), script, TestContext.Current.CancellationToken);
                Assert.False(state.IsError);
                var json = Assert.IsType<JsonElement>(state.Result?.ConvertShellObject(DataType.Json));
                Assert.Equal(1, Assert.Single(json.EnumerateArray()).GetInt32());
                Assert.False(state.HasPipelineInput);
            }
            else
            {
                var exception = await Record.ExceptionAsync(
                    () => shell.RunCommandAsync(new CommandState(), script, TestContext.Current.CancellationToken));
                Assert.NotNull(exception);
                Assert.Contains("requires piped JSON input", exception.ToString());
            }
        }
        finally
        {
            File.Delete(path);
            File.Delete(outerPath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpressionScript_DoesNotInheritOrConsumeCallerPipelineInput(bool requiresInput)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var input = new ShellJson(JsonSerializer.SerializeToElement(new[] { 1 }));
        var callerState = new CommandState { Result = input, HasPipelineInput = true };
        var path = Path.Combine(Path.GetTempPath(), $"cosmosshell-expression-{Guid.NewGuid():N}.csh");
        try
        {
            await File.WriteAllTextAsync(path, requiresInput ? "filter '.'" : "echo", TestContext.Current.CancellationToken);
            var expression = new CommandExpression(new Token(TokenType.Identifier, path, 0, path.Length));
            if (requiresInput)
            {
                var exception = await Record.ExceptionAsync(
                    () => expression.RunScriptAsync(shell, callerState, TestContext.Current.CancellationToken));
                Assert.NotNull(exception);
                Assert.Contains("requires piped JSON input", exception.ToString());
            }
            else
            {
                var state = await expression.RunScriptAsync(shell, callerState, TestContext.Current.CancellationToken);
                Assert.Equal(string.Empty, state.Result?.ConvertShellObject(DataType.Text));
            }

            Assert.Same(input, callerState.Result);
            Assert.True(callerState.HasPipelineInput);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("cd /db/items", false, false)]
    [InlineData("print safe-a p1", false, false)]
    [InlineData("cd /db/items", true, false)]
    [InlineData("print safe-a p1", true, false)]
    [InlineData("echo '{\"id\":\"safe-a\",\"pk\":\"p1\"}' | filter '.' |", false, true)]
    [InlineData("echo '{\"id\":\"safe-a\",\"pk\":\"p1\"}' | filter '.' |", true, true)]
    public async Task ExactIdPreview_UsesPointReadUnlessExplicitlyPiped(string prefix, bool fileScript, bool piped)
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var client = Substitute.For<CosmosClient>();
        var database = Substitute.For<Database>();
        var container = Substitute.For<Container>();
        client.GetDatabase("db").Returns(database);
        database.GetContainer("items").Returns(container);
        var databaseResponse = Substitute.For<DatabaseResponse>();
        databaseResponse.StatusCode.Returns(HttpStatusCode.OK);
        database.ReadAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>()).Returns(databaseResponse);
        var metadata = Substitute.For<ContainerResponse>();
        metadata.StatusCode.Returns(HttpStatusCode.OK);
        metadata.Resource.Returns(new ContainerProperties("items", "/pk"));
        container.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>()).Returns(metadata);
        container.ReadItemStreamAsync("safe-a", new PartitionKey("p1"), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var response = new ResponseMessage(HttpStatusCode.OK)
                {
                    Content = new MemoryStream(Encoding.UTF8.GetBytes("""{"id":"safe-a","pk":"p1"}""")),
                };
                response.Headers.Add("etag", "\"current-etag\"");
                return response;
            });
        shell.State = new ContainerState("items", "db", client);
        var previousState = ShellInterpreter.Instance.State;
        var script = prefix + "\nrm safe-a --key=id --partition-key=p1 --dry-run";
        var path = Path.Combine(Path.GetTempPath(), $"cosmosshell-pipeline-{Guid.NewGuid():N}.csh");
        var beforePath = path + ".before.json";
        var afterPath = path + ".after.json";
        try
        {
            if (fileScript)
            {
                if (!piped)
                {
                    script = $"{prefix} > {ShellLiteral.Quote(beforePath)}\nrm safe-a --key=id --partition-key=p1 --dry-run > {ShellLiteral.Quote(afterPath)}";
                }

                await File.WriteAllTextAsync(path, script, TestContext.Current.CancellationToken);
                script = "exec " + ShellLiteral.Quote(path);
            }

            var state = await shell.RunCommandAsync(new CommandState(), script, TestContext.Current.CancellationToken);
            Assert.False(state.IsError, (state as ErrorCommandState)?.Exception.ToString());
            var result = Assert.IsType<ShellJson>(state.Result).Value;
            Assert.Equal(1, result.GetProperty("count").GetInt32());
            if (piped)
            {
                Assert.False(result.TryGetProperty("id", out _));
                Assert.False(result.TryGetProperty("etag", out _));
                await container.DidNotReceiveWithAnyArgs().ReadItemStreamAsync(default!, default, default, TestContext.Current.CancellationToken);
            }
            else
            {
                Assert.Equal("safe-a", result.GetProperty("id").GetString());
                Assert.Equal("p1", result.GetProperty("partitionKey").GetString());
                Assert.Equal("\"current-etag\"", result.GetProperty("etag").GetString());
                await container.Received(prefix.StartsWith("print", StringComparison.Ordinal) ? 2 : 1)
                    .ReadItemStreamAsync("safe-a", new PartitionKey("p1"), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>());
                if (fileScript)
                {
                    using var preview = JsonDocument.Parse(await File.ReadAllTextAsync(afterPath, TestContext.Current.CancellationToken));
                    Assert.Equal("safe-a", preview.RootElement.GetProperty("id").GetString());
                    Assert.Equal("\"current-etag\"", preview.RootElement.GetProperty("etag").GetString());
                }
            }

            await container.DidNotReceiveWithAnyArgs().DeleteItemAsync<object>(default!, default, default, TestContext.Current.CancellationToken);
            container.DidNotReceiveWithAnyArgs().GetItemQueryStreamIterator(default(string)!, default, default);
        }
        finally
        {
            ShellInterpreter.Instance.State = previousState;
            File.Delete(path);
            File.Delete(beforePath);
            File.Delete(afterPath);
        }
    }
}
