// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Microsoft.Azure.Cosmos;
using NSubstitute;

[Collection(ConsoleOutputTestCollection.Name)]
public class ImportDryRunValidationTests
{
    public static TheoryData<string, bool> IdLengthCases => new()
    {
        { new string('a', 1023), true },
        { new string('a', 1024), false },
        { new string('\u00e4', 511) + "a", true },
        { new string('\u00e4', 512), false },
        { string.Concat(Enumerable.Repeat("\U0001F680", 255)) + "abc", true },
        { string.Concat(Enumerable.Repeat("\U0001F680", 256)), false },
    };

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":null}")]
    [InlineData("{\"id\":42}")]
    [InlineData("{\"id\":true}")]
    [InlineData("{\"id\":[]}")]
    [InlineData("{\"id\":{}}")]
    [InlineData("{\"id\":\"\"}")]
    [InlineData("{\"id\":\"a/b\"}")]
    [InlineData("{\"id\":\"a\\\\b\"}")]
    public async Task InvalidId_FailsWithUsageErrorAndNoWrites(string item)
    {
        var state = await RunDryRunAsync($"[{{\"id\":\"first\"}},{item}]", "json", continueOnError: true);

        Assert.IsType<StructuredErrorCommandState>(state);
        Assert.Equal(ShellExitCode.UsageError, state.ExitCode);
        var result = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.Equal(1, result.GetProperty("validated").GetInt32());
        Assert.Equal(0, result.GetProperty("imported").GetInt32());
        Assert.Equal(0, result.GetProperty("failed").GetInt32());
        Assert.Contains("id", result.GetProperty("error").GetString());
        Assert.Contains("Line 2", result.GetProperty("error").GetString());
        AssertOfflineScope(result);
    }

    [Theory]
    [MemberData(nameof(IdLengthCases))]
    public async Task IdLength_UsesUtf8BytesRatherThanCharacters(string id, bool valid)
    {
        var state = await RunDryRunAsync(JsonSerializer.Serialize(new { id }), "json");

        Assert.Equal(!valid, state.IsError);
        var result = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.Equal(valid ? 1 : 0, result.GetProperty("validated").GetInt32());
        Assert.Equal(0, result.GetProperty("imported").GetInt32());
        AssertOfflineScope(result);
    }

    [Theory]
    [InlineData("json", "{\"id\":\"a\",\"pk\":[]}", 1)]
    [InlineData("jsonl", "{\"id\":\"a\"}\n{\"id\":\"b\"}\n", 2)]
    [InlineData("csv", "id\na\nb\n", 2)]
    [InlineData("json", "[]", 0)]
    public async Task Success_ReportsOfflineValidationWithoutAccessingConnectedTarget(string extension, string content, int expected)
    {
        var state = await RunDryRunAsync(content, extension);

        Assert.False(state.IsError);
        var result = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.Equal(expected, result.GetProperty("validated").GetInt32());
        Assert.Equal(0, result.GetProperty("imported").GetInt32());
        AssertOfflineScope(result);

        var mcp = McpResponseFactory.CreateSuccess(state, new DisconnectedState());
        Assert.NotEqual(true, mcp.IsError);
        var payload = Assert.IsType<JsonElement>(mcp.StructuredContent);
        Assert.True(JsonElement.DeepEquals(result, payload.GetProperty("result")));
    }

    [Theory]
    [InlineData("name\nmissing-id\n")]
    [InlineData("id\n\"\"\n")]
    [InlineData("id\nbad/id\n")]
    public async Task CsvIds_AreValidatedAfterMapping(string content)
    {
        var state = await RunDryRunAsync(content, "csv");

        Assert.IsType<StructuredErrorCommandState>(state);
        Assert.Equal(ShellExitCode.UsageError, state.ExitCode);
        Assert.Equal(0, Assert.IsType<ShellJson>(state.Result).Value.GetProperty("validated").GetInt32());
    }

    private static async Task<CommandState> RunDryRunAsync(string content, string extension, bool continueOnError = false)
    {
        var file = Path.Join(Path.GetTempPath(), $"cosmos-dry-run-{Guid.NewGuid():N}.{extension}");
        using var shell = ShellInterpreter.CreateInstance();
        var client = Substitute.For<CosmosClient>();
        shell.State = new ConnectedState(client);
        try
        {
            await File.WriteAllTextAsync(file, content, TestContext.Current.CancellationToken);
            var state = await new ImportCommand
            {
                File = file,
                DryRun = true,
                Database = "unchecked-db",
                Container = "unchecked-container",
                ContinueOnError = continueOnError,
            }.ExecuteAsync(shell, new CommandState(), "import", TestContext.Current.CancellationToken);

            Assert.Empty(client.ReceivedCalls());
            Assert.Null(state.RequestCharge);
            return state;
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void AssertOfflineScope(JsonElement result)
    {
        Assert.True(result.GetProperty("dryRun").GetBoolean());
        Assert.Equal("offline", result.GetProperty("validationScope").GetString());
        Assert.False(result.GetProperty("targetValidated").GetBoolean());
        Assert.Equal(0, result.GetProperty("requestCharge").GetDouble());
    }
}
