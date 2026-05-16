// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Lsp.Semantics;
using Azure.Data.Cosmos.Shell.Parser;
using Microsoft.Azure.Cosmos;

public class ConnectCommandTests
{
    [Fact]
    public async Task ConnectAsync_CanceledToken_CancelsConnectionAttempt()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shell.ConnectAsync(
            "AccountEndpoint=https://127.0.0.1:1/;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;",
            mode: ConnectionMode.Gateway,
            token: cancellationTokenSource.Token));
    }

    [Fact]
    public async Task ConnectCommand_VSCodeCredentialOption_BindsHiddenInteractiveFlag()
    {
        var command = await BindConnectCommandAsync("connect https://example.documents.azure.com:443/ -vscode-credential");

        Assert.Equal("https://example.documents.azure.com:443/", command.ConnectionString);
        Assert.True(command.UseVSCodeCredential);
    }

    [Fact]
    public async Task ConnectCommand_StartupVSCodeCredentialOptionAlias_BindsHiddenInteractiveFlag()
    {
        var command = await BindConnectCommandAsync("connect https://example.documents.azure.com:443/ --connect-vscode-credential");

        Assert.Equal("https://example.documents.azure.com:443/", command.ConnectionString);
        Assert.True(command.UseVSCodeCredential);
    }

    [Fact]
    public void ConnectCommand_VSCodeCredentialOption_IsHiddenButKnownToCommandMetadata()
    {
        Assert.True(CommandFactory.TryCreateFactory(typeof(ConnectCommand), out var factory));

        Assert.DoesNotContain(factory.Options, option => option.MatchesArgument("vscode-credential"));
        Assert.Contains(factory.AllOptions, option => option.MatchesArgument("vscode-credential"));
        Assert.True(factory.HasOption("vscode-credential"));

        using var shell = ShellInterpreter.CreateInstance();
        Assert.True(shell.App.IsOptionPrefix("connect", "vscode-credential"));
    }

    [Fact]
    public void ConnectCommand_VSCodeCredentialOption_DoesNotProduceUnknownOptionDiagnostic()
    {
        const string CommandText = "connect https://example.documents.azure.com:443/ -vscode-credential";
        var parser = new StatementParser(CommandText);
        var statements = parser.ParseStatements();

        var model = new SemanticAnalyzer().Analyze(statements, CommandText);

        Assert.DoesNotContain(model.Diagnostics, diagnostic => diagnostic.Code == "SEM002");
    }

    [Fact]
    public void LocalEmulatorConnectionFailureMessage_ExplainsCommonCauses()
    {
        var endpoint = new Uri("https://localhost:8081/");
        var message = ShellInterpreter.GetLocalEmulatorConnectionFailureMessage(endpoint);

        Assert.Contains("Cosmos DB emulator at https://localhost:8081/", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("http://localhost:8081/", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--protocol [https|http]", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://learn.microsoft.com/en-us/azure/cosmos-db/emulator-linux", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docker ps", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("http://localhost:8080/alive", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docker run", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalEmulatorConnectionFailureMessage_SuggestsHttpsWhenHttpFails()
    {
        var endpoint = new Uri("http://localhost:8081/");
        var message = ShellInterpreter.GetLocalEmulatorConnectionFailureMessage(endpoint);

        Assert.Contains("Cosmos DB emulator at http://localhost:8081/", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://localhost:8081/", message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ConnectCommand> BindConnectCommandAsync(string commandText)
    {
        var parser = new StatementParser(commandText);
        var statement = Assert.IsType<CommandStatement>(Assert.Single(parser.ParseStatements()));

        Assert.True(CommandFactory.TryCreateFactory(typeof(ConnectCommand), out var factory));
        using var shell = ShellInterpreter.CreateInstance();
        var command = await statement.CreateCommandAsync(factory, shell, new CommandState(), CancellationToken.None);
        return Assert.IsType<ConnectCommand>(command);
    }
}
