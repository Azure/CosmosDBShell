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
/// Offline unit tests for <see cref="WhoamiCommand"/>. These cover the
/// not-connected branch and the key/emulator branch where no Entra identity is
/// available. The token-decoding path is exercised through <see cref="JwtClaims"/>
/// tests; live-identity introspection is covered by integration tests.
/// </summary>
public class WhoamiCommandTests
{
    [Fact]
    public async Task Whoami_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new WhoamiCommand();

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "whoami", CancellationToken.None));
    }

    [Fact]
    public async Task Whoami_KeyAuth_ReportsNoIdentity()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.StdOutRedirect = "out.txt";
        shell.Connect(CreateTestClient(), credentialTypeOverride: "AccountKey");
        var command = new WhoamiCommand();

        var state = await command.ExecuteAsync(shell, new CommandState(), "whoami", CancellationToken.None);

        var json = Assert.IsType<ShellJson>(state.Result).Value;
        Assert.Equal("AccountKey", json.GetProperty("credentialType").GetString());
        Assert.False(json.GetProperty("identityAvailable").GetBoolean());
        Assert.Equal(
            MessageService.GetString("command-whoami-key-auth-note"),
            json.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Whoami_JsonFormat_SetsJsonOutput()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Connect(CreateTestClient(), credentialTypeOverride: "AccountKey");
        var command = new WhoamiCommand { OutputFormat = "json" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "whoami --format=json", CancellationToken.None);

        Assert.Equal(OutputFormat.JSon, state.OutputFormat);
        Assert.NotNull(state.RenderUser);
        Assert.Contains("\"credentialType\"", state.GenerateOutputText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Whoami_CsvFormat_SetsCsvOutput()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Connect(CreateTestClient(), credentialTypeOverride: "AccountKey");
        var command = new WhoamiCommand { OutputFormat = "csv" };

        var state = await command.ExecuteAsync(shell, new CommandState(), "whoami --format=csv", CancellationToken.None);

        Assert.Equal(OutputFormat.CSV, state.OutputFormat);
        Assert.NotNull(state.RenderUser);

        var csv = state.GenerateOutputText();
        Assert.Contains("credentialType", csv, StringComparison.Ordinal);
        Assert.Contains("AccountKey", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Whoami_InvalidFormat_Throws()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.Connect(CreateTestClient(), credentialTypeOverride: "AccountKey");
        var command = new WhoamiCommand { OutputFormat = "xml" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => command.ExecuteAsync(shell, new CommandState(), "whoami --format=xml", CancellationToken.None));
    }

    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }
}
