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
using Spectre.Console;

[Collection(CosmosShell.Tests.Shell.ThemeStateTestCollection.Name)]
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
    public async Task ConnectAsync_AzureCliWithManagedIdentity_ThrowsConflict()
    {
        // #1: a credential the user explicitly requested must never be silently
        // ignored. --azure-cli and --managed-identity select different credentials.
        using var shell = ShellInterpreter.CreateInstance();

        var ex = await Assert.ThrowsAsync<ShellException>(() => shell.ConnectAsync(
            "https://example.documents.azure.com:443/",
            credentialMethod: CredentialMethod.AzureCli,
            managedIdentityClientId: "00000000-0000-0000-0000-000000000000",
            token: TestContext.Current.CancellationToken));

        Assert.Contains("--azure-cli", ex.Message, StringComparison.Ordinal);
        Assert.Contains("--managed-identity", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectAsync_AccountKeyWithAzureCli_ThrowsConflict()
    {
        // An account key in the connection string and an explicit credential method
        // are mutually exclusive; the validation runs before any network call.
        using var shell = ShellInterpreter.CreateInstance();

        var ex = await Assert.ThrowsAsync<ShellException>(() => shell.ConnectAsync(
            "AccountEndpoint=https://example.documents.azure.com:443/;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;",
            credentialMethod: CredentialMethod.AzureCli,
            token: TestContext.Current.CancellationToken));

        Assert.Contains("--azure-cli", ex.Message, StringComparison.Ordinal);
        Assert.Contains("account key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectAsync_AccountKeyWithTenant_ThrowsConflict()
    {
        using var shell = ShellInterpreter.CreateInstance();

        var ex = await Assert.ThrowsAsync<ShellException>(() => shell.ConnectAsync(
            "AccountEndpoint=https://example.documents.azure.com:443/;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;",
            tenantId: "00000000-0000-0000-0000-000000000000",
            token: TestContext.Current.CancellationToken));

        Assert.Contains("account key", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task ConnectCommand_AzureCliOption_BindsInteractiveFlag()
    {
        var command = await BindConnectCommandAsync("connect https://example.documents.azure.com:443/ -azure-cli");

        Assert.Equal("https://example.documents.azure.com:443/", command.ConnectionString);
        Assert.True(command.UseAzureCli);
    }

    [Fact]
    public void ConnectCommand_AzureCliOption_IsKnownToCommandMetadata()
    {
        Assert.True(CommandFactory.TryCreateFactory(typeof(ConnectCommand), out var factory));

        Assert.Contains(factory.AllOptions, option => option.MatchesArgument("azure-cli"));
        Assert.True(factory.HasOption("azure-cli"));
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

    [Fact]
    public void WriteConnectionError_NonVerbose_ShowsMessageInnerReasonAndVerboseHint()
    {
        // Issue: a failed connection printed only "Failed to connect to the Cosmos DB
        // account." with no indication of the underlying cause. The inner exception
        // chain must be surfaced so the user can see why the connection failed.
        var failure = new ShellException(
            MessageService.GetString("error-connection_failed"),
            new InvalidOperationException("Response status code does not indicate success: 401 (Unauthorized)."));

        var output = CaptureConsole(() => ShellInterpreter.WriteConnectionError(failure, verbose: false));

        Assert.Contains("Failed to connect to the Cosmos DB account.", output, StringComparison.Ordinal);
        Assert.Contains("Response status code does not indicate success: 401 (Unauthorized).", output, StringComparison.Ordinal);
        Assert.Contains(MessageService.GetString("shell-connect-verbose-hint"), output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteConnectionError_NonVerbose_WalksFullInnerExceptionChain()
    {
        var failure = new ShellException(
            "outer",
            new InvalidOperationException("middle", new InvalidOperationException("root cause")));

        var output = CaptureConsole(() => ShellInterpreter.WriteConnectionError(failure, verbose: false));

        Assert.Contains("outer", output, StringComparison.Ordinal);
        Assert.Contains("middle", output, StringComparison.Ordinal);
        Assert.Contains("root cause", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteConnectionError_Verbose_RendersFullExceptionDetails()
    {
        var failure = new ShellException(
            "outer failure",
            new InvalidOperationException("verbose-only inner detail"));

        var output = CaptureConsole(() => ShellInterpreter.WriteConnectionError(failure, verbose: true));

        Assert.Contains("verbose-only inner detail", output, StringComparison.Ordinal);
        Assert.Contains(nameof(ShellException), output, StringComparison.Ordinal);
        Assert.DoesNotContain(MessageService.GetString("shell-connect-verbose-hint"), output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteConnectionError_Verbose_SurfacesCosmosRequestCoordinates()
    {
        // Issue: a 403 authorization denial was indistinguishable from a token or
        // network failure. In verbose mode the Cosmos HTTP status, sub-status, and
        // activity id must be surfaced up front so the failure category is obvious.
        var cosmosException = new CosmosException(
            "Request blocked by auth.",
            System.Net.HttpStatusCode.Forbidden,
            subStatusCode: 5301,
            activityId: "8b1f0000-0000-0000-0000-000000000000",
            requestCharge: 0);
        var failure = new ShellException(
            MessageService.GetString("error-connection_failed"),
            cosmosException);

        var output = CaptureConsole(() => ShellInterpreter.WriteConnectionError(failure, verbose: true));

        Assert.Contains("403", output, StringComparison.Ordinal);
        Assert.Contains("5301", output, StringComparison.Ordinal);
        Assert.Contains("8b1f0000-0000-0000-0000-000000000000", output, StringComparison.Ordinal);
    }

    private static string CaptureConsole(Action action)
    {
        var saved = AnsiConsole.Console;
        using var writer = new StringWriter();
        try
        {
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(writer),
            });

            action();
        }
        finally
        {
            AnsiConsole.Console = saved;
        }

        return writer.ToString();
    }
}
