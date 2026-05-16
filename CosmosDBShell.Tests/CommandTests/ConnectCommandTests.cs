// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Lsp.Semantics;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
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

    private static async Task<ConnectCommand> BindConnectCommandAsync(string commandText)
    {
        var parser = new StatementParser(commandText);
        var statement = Assert.IsType<CommandStatement>(Assert.Single(parser.ParseStatements()));

        Assert.True(CommandFactory.TryCreateFactory(typeof(ConnectCommand), out var factory));
        using var shell = ShellInterpreter.CreateInstance();
        var command = await statement.CreateCommandAsync(factory, shell, new CommandState(), CancellationToken.None);
        return Assert.IsType<ConnectCommand>(command);
    }

    [Fact]
    public void ConnectCommand_NotConnectedUsageHint_LocalizationKeysAreDefined()
    {
        // Issue #81: running `connect` while disconnected used to print only
        // "Not connected" with no hint about how to authenticate. The hint
        // strings must resolve to non-empty values.
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("command-connect-not_connected-usage-header")));
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("command-connect-not_connected-usage-footer")));
        Assert.False(string.IsNullOrWhiteSpace(MessageService.GetString("shell-not_connected_hint")));
    }

    [Fact]
    public void ConnectCommand_PrintConnectUsageHint_HasExamplesToPrint()
    {
        // The hint helper iterates the connect command's CosmosExample metadata and
        // skips the bare `connect` no-arg form. Confirm there is at least one other
        // example to display so the helper output is meaningful.
        Assert.True(CommandFactory.TryCreateFactory(typeof(ConnectCommand), out var factory));

        var examples = factory.ExamplesWithDescriptions
            .Where(e => !string.IsNullOrWhiteSpace(e.Example) && e.Example != "connect")
            .ToList();

        Assert.NotEmpty(examples);
    }

    [Fact]
    public void ConnectCommand_PrintConnectUsageHint_RunsWithoutThrowing()
    {
        using var shell = ShellInterpreter.CreateInstance();

        // Smoke test: must not throw even when the shell's command map exposes the
        // factory through ShellInterpreter.App.Commands.
        var ex = Record.Exception(() => ConnectCommand.PrintConnectUsageHint(shell));
        Assert.Null(ex);
    }
}
