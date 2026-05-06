// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Lsp.Semantics;
using Azure.Data.Cosmos.Shell.Parser;
using Microsoft.Azure.Cosmos;
using System.Net.Http;

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
    public async Task ConnectCommand_EmulatorOption_BindsFlag()
    {
        var command = await BindConnectCommandAsync("connect --emulator");

        Assert.Null(command.ConnectionString);
        Assert.True(command.Emulator);
    }

    [Fact]
    public async Task ConnectCommand_EmulatorShortOption_BindsFlag()
    {
        var command = await BindConnectCommandAsync("connect -e");

        Assert.True(command.Emulator);
    }

    [Fact]
    public async Task ConnectCommand_EmulatorWithExplicitEndpoint_BindsBoth()
    {
        var command = await BindConnectCommandAsync("connect --emulator https://localhost:9000/");

        Assert.Equal("https://localhost:9000/", command.ConnectionString);
        Assert.True(command.Emulator);
    }

    [Fact]
    public async Task ConnectAsync_EmulatorAgainstNonLocalEndpoint_Throws()
    {
        using var shell = ShellInterpreter.CreateInstance();

        var ex = await Assert.ThrowsAsync<ShellException>(() => shell.ConnectAsync(
            "https://contoso.documents.azure.com:443/",
            forceEmulator: true,
            token: CancellationToken.None));
        Assert.Contains("emulator", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsTlsHandshakeFailure_DetectsAuthenticationException()
    {
        var ex = new HttpRequestException("send failed", new System.Security.Authentication.AuthenticationException("inner"));
        Assert.True(ShellInterpreter.IsTlsHandshakeFailure(ex));
    }

    [Fact]
    public void IsTlsHandshakeFailure_DetectsResetSocket()
    {
        var ex = new HttpRequestException("send failed", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset));
        Assert.True(ShellInterpreter.IsTlsHandshakeFailure(ex));
    }

    [Fact]
    public void IsTlsHandshakeFailure_IgnoresGenericHttpRequestException()
    {
        // No type-based marker => not a TLS handshake failure (was previously matched by the
        // brittle "SSL" message substring check).
        var ex = new HttpRequestException("Connection refused (SSL inside the message text only)");
        Assert.False(ShellInterpreter.IsTlsHandshakeFailure(ex));
    }

    [Fact]
    public void IsTlsHandshakeFailure_IgnoresUnrelatedSocketErrors()
    {
        var ex = new HttpRequestException("send failed", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostUnreachable));
        Assert.False(ShellInterpreter.IsTlsHandshakeFailure(ex));
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
