// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Offline unit tests for <see cref="CanICommand"/>. These cover input
/// validation, the non-probeable 'manage' action, and the master-key branch.
/// The live data-plane probes (read/query/write) are exercised by the emulator
/// integration tests.
/// </summary>
public class CanICommandTests
{
    [Fact]
    public async Task CanI_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new CanICommand { Action = "read" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "can-i read", CancellationToken.None));
    }

    [Fact]
    public async Task CanI_InvalidAction_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new CanICommand { Action = "delete" };

        var exception = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "can-i delete", CancellationToken.None));

        Assert.Equal(
            MessageService.GetArgsString("command-can-i-invalid-action", "action", "delete"),
            exception.Message);
    }

    [Fact]
    public async Task CanI_Manage_ReportsIndeterminate()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.StdOutRedirect = "out.txt";
        shell.Connect(CreateTestClient(), credentialTypeOverride: "AccountKey");
        var command = new CanICommand { Action = "manage" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "can-i manage", CancellationToken.None);

        var json = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.Equal("manage", json.GetProperty("action").GetString());
        Assert.Equal("indeterminate", json.GetProperty("decision").GetString());
        Assert.Equal("none", json.GetProperty("method").GetString());
    }

    [Fact]
    public async Task CanI_ReadWithoutContainer_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new CanICommand { Action = "read" };

        var exception = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "can-i read", CancellationToken.None));

        Assert.Equal(
            MessageService.GetString("command-can-i-requires-container"),
            exception.Message);
    }

    [Fact]
    public async Task CanI_KeyAuth_ReportsAllow()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.StdOutRedirect = "out.txt";
        shell.Connect(CreateTestClient(), credentialTypeOverride: "AccountKey");
        var command = new CanICommand { Action = "read", Database = "MyDB", Container = "Products" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "can-i read --database=MyDB --container=Products", CancellationToken.None);

        var json = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.Equal("allow", json.GetProperty("decision").GetString());
        Assert.Equal("key", json.GetProperty("method").GetString());
        Assert.Equal("MyDB", json.GetProperty("database").GetString());
        Assert.Equal("Products", json.GetProperty("container").GetString());
    }

    [Fact]
    public async Task CanI_CsvFormat_SetsCsvOutput()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Connect(CreateTestClient(), credentialTypeOverride: "AccountKey");
        var command = new CanICommand { Action = "read", Database = "MyDB", Container = "Products", OutputFormat = "csv" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "can-i read --database=MyDB --container=Products --format=csv", CancellationToken.None);

        Assert.Equal(OutputFormat.CSV, state.OutputFormat);
        Assert.NotNull(state.RenderUser);

        var csv = state.GenerateOutputText();
        Assert.Contains("decision", csv, StringComparison.Ordinal);
        Assert.Contains("allow", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CanI_JsonFormat_SetsJsonOutput()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Connect(CreateTestClient(), credentialTypeOverride: "AccountKey");
        var command = new CanICommand { Action = "manage", OutputFormat = "json" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "can-i manage --format=json", CancellationToken.None);

        Assert.Equal(OutputFormat.JSon, state.OutputFormat);
        Assert.NotNull(state.RenderUser);
        Assert.Contains("\"decision\"", state.GenerateOutputText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CanI_InvalidFormat_Throws()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Connect(CreateTestClient(), credentialTypeOverride: "AccountKey");
        var command = new CanICommand { Action = "manage", OutputFormat = "xml" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => command.ExecuteAsync(shell, new CommandState(), "can-i manage --format=xml", CancellationToken.None));
    }

    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }
}
