// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;

[CosmosCommand("connect")]
[CosmosExample("connect", Description = "Show current connection information and mode")]
[CosmosExample("connect \"AccountEndpoint=https://myaccount.documents.azure.com:443/;AccountKey=mykey;\"", Description = "Connect using connection string with account key")]
[CosmosExample("connect https://localhost:8081", Description = "Connect to the local Cosmos DB Emulator (uses well-known key and gateway mode)")]
[CosmosExample("connect https://myaccount.documents.azure.com:443/ -hint=user@contoso.com", Description = "Connect using Azure AD authentication with login hint")]
[CosmosExample("connect https://myaccount.documents.azure.com:443/ -cred=entraid:tenant-id -mode=gateway", Description = "Connect using Entra ID with gateway connection mode")]
internal partial class ConnectCommand : CosmosCommand
{
    // internal static readonly string EntraRedirectUrl = "https://login.microsoftonline.com/common/oauth2/nativeclient";
    internal static readonly string EntraRedirectUrl = "http://localhost";
    private static readonly Regex PrincipalIdRegex = new("Request is blocked because principal \\[(.*)\\] does not have required RBAC permissions permissions to perform action \\[(.*)\\]");

    [CosmosParameter("connectionString", IsRequired = false)]
    public string? ConnectionString { get; init; }

    [CosmosOption("hint")]
    public string? LoginHint { get; set; }

    [CosmosOption("tenant")]
    public string? TenantId { get; set; }

    [CosmosOption("mode")]
    public string? Mode { get; set; }

    [CosmosOption("host")]
    public string? AuthorityHost { get; set; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        // If no connection string provided, show current connection info
        if (this.ConnectionString is null)
        {
            return await PrintConnectionInfoAsync(shell, commandState, token);
        }

        // If already connected, inform user about switching accounts.
        // Do NOT dispose the old client here — ShellInterpreter.Connect() will
        // dispose the previous state only after the new connection succeeds.
        if (shell.State is ConnectedState cs)
        {
            var previousEndpoint = cs.Client.Endpoint.Host;
            AnsiConsole.MarkupLine(MessageService.GetArgsString("command-connect-switching", "endpoint", previousEndpoint));
        }

        ConnectionMode? connectionMode = null;
        if (!string.IsNullOrWhiteSpace(this.Mode))
        {
            string trimmedMode = this.Mode.Trim();
            if (trimmedMode.Equals("direct", StringComparison.OrdinalIgnoreCase))
            {
                connectionMode = ConnectionMode.Direct;
            }
            else if (trimmedMode.Equals("gateway", StringComparison.OrdinalIgnoreCase))
            {
                connectionMode = ConnectionMode.Gateway;
            }
            else
            {
                throw new CommandException(
                    "connect",
                    $"Invalid mode '{this.Mode}'. Valid values are 'direct' or 'gateway'.");
            }
        }

        try
        {
            // Default to Gateway mode for emulator if no explicit mode was specified
            if (connectionMode is null && ParsedDocDBConnectionString.IsLocalEmulatorEndpoint(this.ConnectionString))
            {
                connectionMode = ConnectionMode.Gateway;
            }

            await shell.ConnectAsync(this.ConnectionString, this.LoginHint, null, null, connectionMode, tenantId: this.TenantId, authorityHost: this.AuthorityHost);
            var returnState = new CommandState
            {
                IsPrinted = true,
            };
            var jsonString = $"{{\"connected state\": \"{this.ConnectionString}\"}}";
            using var jsonDoc = JsonDocument.Parse(jsonString);
            returnState.Result = new ShellJson(jsonDoc.RootElement.Clone());
            return returnState;
        }
        catch (Exception e)
        {
            if (TryGetPrincipalIdFromRbacException(e, out var id, out var permission))
            {
                AskForRBacPermissions(id ?? string.Empty, permission ?? string.Empty);
                return commandState;
            }

            throw new CommandException("connect", e);
        }
    }

    internal static bool TryGetPrincipalIdFromRbacException(Exception e, out string? principalId, out string? permission)
    {
        var match = PrincipalIdRegex.Match(e.Message);
        if (match.Success)
        {
            principalId = match.Groups[1].Value;
            permission = match.Groups[2].Value;
            return true;
        }

        principalId = null;
        permission = null;
        return false;
    }

    internal static void AskForRBacPermissions(string principalId, string permission)
    {
        AnsiConsole.Markup($"[red]{MessageService.GetString("error")}[/] ");
        ShellInterpreter.WriteLine(MessageService.GetArgsString("command-connect-rbac-error", "id", principalId, "permission", permission));
    }

    private static async Task<CommandState> PrintConnectionInfoAsync(ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        if (shell.State is not ConnectedState connectedState)
        {
            AnsiConsole.MarkupLine(MessageService.GetString("command-connect-not_connected"));
            commandState.IsPrinted = true;
            var notConnectedJson = new Dictionary<string, object?>
            {
                ["connected"] = false,
            };
            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(notConnectedJson));
            return commandState;
        }

        var client = connectedState.Client;

        var acc = await client.ReadAccountAsync();
        AnsiConsole.MarkupLine($"[bold]{MessageService.GetString("command-connect-info-title")}[/]");

        var table = new Table();
        table.AddColumns(string.Empty, string.Empty);
        table.HideHeaders();

        table.AddRow(MessageService.GetString("command-connect-info-account"), $"[white]{acc.Id}[/]");
        table.AddRow(MessageService.GetString("command-connect-info-endpoint"), $"[white]{client.Endpoint}[/]");

        // Display the connection mode
        var connectionMode = client.ClientOptions.ConnectionMode;
        table.AddRow(MessageService.GetString("command-connect-info-mode"), $"[white]{connectionMode}[/]");

        // Display the readable/writable regions
        table.AddRow(MessageService.GetString("command-connect-info-read-regions"), $"[white]{string.Join(", ", acc.ReadableRegions.Select(r => r.Name))}[/]");
        table.AddRow(MessageService.GetString("command-connect-info-write-regions"), $"[white]{string.Join(", ", acc.WritableRegions.Select(r => r.Name))}[/]");

        // Show current navigation state
        string currentLocation;
        if (shell.State is ContainerState containerState)
        {
            currentLocation = $"/{containerState.DatabaseName}/{containerState.ContainerName}";
        }
        else if (shell.State is DatabaseState databaseState)
        {
            currentLocation = $"/{databaseState.DatabaseName}";
        }
        else
        {
            currentLocation = "/";
        }

        table.AddRow(MessageService.GetString("command-connect-info-location"), $"[cyan]{currentLocation}[/]");

        AnsiConsole.Write(table);

        commandState.IsPrinted = true;
        var jsonResult = new Dictionary<string, object?>
        {
            ["connected"] = true,
            ["accountId"] = acc.Id,
            ["endpoint"] = client.Endpoint.ToString(),
            ["connectionMode"] = connectionMode.ToString(),
            ["readRegions"] = acc.ReadableRegions.Select(r => r.Name).ToArray(),
            ["writeRegions"] = acc.WritableRegions.Select(r => r.Name).ToArray(),
            ["currentLocation"] = currentLocation,
        };
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(jsonResult));
        return commandState;
    }
}
