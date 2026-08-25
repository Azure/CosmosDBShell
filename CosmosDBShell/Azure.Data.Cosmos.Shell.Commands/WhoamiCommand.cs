// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Collections.Generic;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Core;
using global::Azure.Identity;
using Spectre.Console;

[CosmosCommand("whoami")]
[CosmosExample("whoami", Description = "Show the authenticated identity and credential type")]
[CosmosExample("whoami --format=json", Description = "Emit the identity as a JSON object")]
[CosmosExample("whoami --format=csv", Description = "Emit the identity as a single CSV row")]
[McpAnnotation(
    Description = @"
Shows the authenticated identity for the current connection.

Reports the credential type (for example DefaultAzureCredential, ManagedIdentityCredential, AccountKey, or Emulator) and,
for Entra ID connections, the principal, tenant, application id, and user principal name decoded from the access token.

Data-plane RBAC role assignments are a control-plane concept and are not reported. Account-key and emulator connections have
no Entra identity, so only the credential type is shown. Use --format (table, json, csv) to control the output.")]
internal class WhoamiCommand : CosmosCommand
{
    private const string CosmosScope = "https://cosmos.azure.com/.default";

    [CosmosOption("format", "f")]
    public string? OutputFormat { get; init; }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (shell.State is not ConnectedState)
        {
            throw new NotConnectedException("whoami");
        }

        var format = this.OutputFormat ?? Environment.GetEnvironmentVariable("COSMOSDB_SHELL_FORMAT");
        commandState.SetFormat(format);

        var credentialType = shell.ActiveCredentialType ?? "Unknown";
        var credential = shell.ActiveCredential;

        var result = new Dictionary<string, object?>
        {
            ["credentialType"] = credentialType,
        };

        if (credential is null)
        {
            // Account-key and emulator connections have no Entra identity to introspect.
            var note = MessageService.GetString("command-whoami-key-auth-note");
            result["identityAvailable"] = false;
            result["note"] = note;
            commandState.RenderUser = () => RenderTable(credentialType, null, note);
        }
        else
        {
            AccessToken accessToken;
            try
            {
                accessToken = await credential.GetTokenAsync(new TokenRequestContext(new[] { CosmosScope }), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (AuthenticationFailedException ex)
            {
                throw new CommandException("whoami", MessageService.GetArgsString("command-whoami-token-error", "message", ex.Message));
            }

            var claims = JwtClaims.TryDecodePayload(accessToken.Token);
            var identity = BuildIdentity(claims, accessToken.ExpiresOn);

            result["identityAvailable"] = true;
            foreach (var pair in identity)
            {
                result[pair.Key] = pair.Value;
            }

            commandState.RenderUser = () => RenderTable(credentialType, identity, null);
        }

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(result));
        return commandState;
    }

    private static Dictionary<string, object?> BuildIdentity(JsonElement? claims, DateTimeOffset expiresOn)
    {
        var principalId = JwtClaims.GetString(claims, "oid");
        var tenantId = JwtClaims.GetString(claims, "tid");
        var applicationId = JwtClaims.GetString(claims, "appid", "azp");
        var userPrincipalName = JwtClaims.GetString(claims, "upn", "preferred_username", "unique_name");
        var displayName = JwtClaims.GetString(claims, "name");
        var identityTypeClaim = JwtClaims.GetString(claims, "idtyp");

        string identityType = identityTypeClaim switch
        {
            "app" => "application",
            "user" => "user",
            _ => userPrincipalName is null ? "application" : "user",
        };

        return new Dictionary<string, object?>
        {
            ["principalId"] = principalId,
            ["tenantId"] = tenantId,
            ["applicationId"] = applicationId,
            ["userPrincipalName"] = userPrincipalName,
            ["displayName"] = displayName,
            ["identityType"] = identityType,
            ["tokenExpiresOn"] = expiresOn.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };
    }

    private static void RenderTable(string credentialType, Dictionary<string, object?>? identity, string? note)
    {
        AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-whoami-title")));

        var table = new Table();
        table.AddColumns(string.Empty, string.Empty);
        table.HideHeaders();

        table.AddRow(MessageService.GetString("command-whoami-credential-type"), Theme.FormatTableValue(credentialType));

        if (identity is not null)
        {
            AddRow(table, "command-whoami-principal-id", identity["principalId"]);
            AddRow(table, "command-whoami-tenant-id", identity["tenantId"]);
            AddRow(table, "command-whoami-application-id", identity["applicationId"]);
            AddRow(table, "command-whoami-user-principal-name", identity["userPrincipalName"]);
            AddRow(table, "command-whoami-display-name", identity["displayName"]);
            AddRow(table, "command-whoami-identity-type", identity["identityType"]);
            AddRow(table, "command-whoami-token-expires", identity["tokenExpiresOn"]);
        }

        AnsiConsole.Write(table);

        if (!string.IsNullOrEmpty(note))
        {
            AnsiConsole.MarkupLine(Theme.FormatMuted(note));
        }
    }

    private static void AddRow(Table table, string labelKey, object? value)
    {
        var text = value as string;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        table.AddRow(MessageService.GetString(labelKey), Theme.FormatTableValue(text));
    }
}
