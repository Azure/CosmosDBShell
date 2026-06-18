//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Net;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using global::Azure.Data.Cosmos.Shell.Core;
using global::Azure.Data.Cosmos.Shell.States;
using Microsoft.Azure.Cosmos.Scripts;
using Spectre.Console;

[CosmosCommand("trigger")]
[CosmosExample("trigger list", Description = "List the triggers in the current container")]
[CosmosExample("trigger show myTrigger", Description = "Display the body of a trigger")]
[CosmosExample("trigger exists myTrigger", Description = "Check whether a trigger exists (usable in if conditions)")]
[CosmosExample("trigger create myTrigger ./myTrigger.js --type pre --operation create", Description = "Create a pre-trigger for create operations")]
[CosmosExample("trigger create myTrigger ./myTrigger.js --type post --operation all --force", Description = "Create or replace a post-trigger for all operations")]
[CosmosExample("trigger edit myTrigger", Description = "Edit a trigger body in an external editor")]
[CosmosExample("trigger delete myTrigger", Description = "Delete a trigger")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(
    Title = "Triggers",
    Description = @"
Manages JavaScript triggers on the current Cosmos DB container through subcommands:
- 'list' returns the trigger ids in the container with their type and operation.
- 'show <name>' returns the body of a trigger.
- 'exists <name>' returns whether a trigger exists.
- 'create <name> <file>' creates a trigger from a JavaScript file. --type selects pre or post, --operation selects all/create/replace/delete/update, and --force replaces an existing one.
- 'edit <name>' opens an existing trigger body in an external editor.
- 'delete <name>' removes a trigger.
This command is restricted in MCP. Run it manually in the shell.
",
    Restricted = true,
    Destructive = true,
    OpenWorld = true)]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class TriggerCommand : CosmosCommand
{
    /// <summary>
    /// When the launched editor returns faster than this, assume it handed the
    /// file to a background instance (for example Windows notepad or 'code'
    /// without --wait) and prompt the user before reading the file back.
    /// </summary>
    private static readonly TimeSpan QuickEditorExit = TimeSpan.FromSeconds(2);

    [CosmosParameter("subcommand", RequiredErrorKey = "command-trigger-error-missing_subcommand")]
    public string Subcommand { get; init; } = string.Empty;

    [CosmosParameter("name", IsRequired = false)]
    public string? Name { get; init; }

    [CosmosParameter("value", IsRequired = false)]
    public string? Value { get; init; }

    [CosmosOption("type", "t")]
    public string? Type { get; init; }

    [CosmosOption("operation", "op")]
    public string? Operation { get; init; }

    [CosmosOption("force", "f")]
    public bool? Force { get; init; }

    [CosmosOption("database", "db")]
    public string? Database { get; init; }

    [CosmosOption("container", "con")]
    public string? Container { get; init; }

    /// <summary>
    /// Normalizes a subcommand token to its canonical lower-case form.
    /// </summary>
    internal static string NormalizeSubcommand(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Parses the <c>--type</c> option into a <see cref="TriggerType"/>. Accepts
    /// 'pre' and 'post' (case-insensitive).
    /// </summary>
    internal static TriggerType ParseTriggerType(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "pre" => TriggerType.Pre,
            "post" => TriggerType.Post,
            _ => throw new CommandException(
                "trigger",
                MessageService.GetArgsString("command-trigger-error-invalid_type", "type", value ?? string.Empty)),
        };
    }

    /// <summary>
    /// Parses the <c>--operation</c> option into a <see cref="TriggerOperation"/>.
    /// Defaults to <see cref="TriggerOperation.All"/> when no value is supplied.
    /// </summary>
    internal static TriggerOperation ParseTriggerOperation(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "all" => TriggerOperation.All,
            "create" => TriggerOperation.Create,
            "replace" => TriggerOperation.Replace,
            "delete" => TriggerOperation.Delete,
            "update" => TriggerOperation.Update,
            _ => throw new CommandException(
                "trigger",
                MessageService.GetArgsString("command-trigger-error-invalid_operation", "operation", value ?? string.Empty)),
        };
    }

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var subcommand = NormalizeSubcommand(this.Subcommand);
        if (subcommand.Length == 0)
        {
            throw new CommandException("trigger", MessageService.GetString("command-trigger-error-missing_subcommand"));
        }

        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("trigger");
        }

        var (_, _, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "trigger",
            token);

        return subcommand switch
        {
            "list" or "ls" => await this.ListAsync(container, commandState, token),
            "show" or "cat" => await this.ShowAsync(container, commandState, token),
            "exists" => await this.ExistsAsync(container, commandState, token),
            "create" or "set" => await this.CreateAsync(container, shell, commandState, token),
            "edit" => await this.EditAsync(container, shell, commandState, token),
            "delete" or "rm" => await this.DeleteAsync(container, commandState, token),
            _ => throw new CommandException(
                "trigger",
                MessageService.GetArgsString("command-trigger-error-invalid_subcommand", "subcommand", subcommand)),
        };
    }

    private static CommandException NotFound(string name, Exception inner) =>
        new("trigger", MessageService.GetArgsString("command-trigger-error-not_found", "name", name), inner);

    /// <summary>
    /// Returns a sample trigger body used to seed the editor when creating a new
    /// trigger without supplying a file. Mirrors the template offered by the
    /// Azure Databases extension for Visual Studio Code.
    /// </summary>
    internal static string DefaultTriggerBody() =>
        """
        function trigger() {
            var context = getContext();
            var request = context.getRequest();

            // Item to be created in the current operation.
            var itemToCreate = request.getBody();

            // Add a timestamp property when one is not present.
            if (!('createdTime' in itemToCreate)) {
                itemToCreate['createdTime'] = new Date().getTime();
            }

            // Update the item that will be created.
            request.setBody(itemToCreate);
        }

        """;

    private async Task<CommandState> ListAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var items = new List<object>();
        var rows = new List<(string Id, string Type, string Operation, int BodyLength)>();
        using var iterator = container.Scripts.GetTriggerQueryIterator<TriggerProperties>();
        while (iterator.HasMoreResults)
        {
            foreach (var properties in await iterator.ReadNextAsync(token))
            {
                items.Add(new
                {
                    id = properties.Id,
                    triggerType = properties.TriggerType.ToString(),
                    triggerOperation = properties.TriggerOperation.ToString(),
                    etag = properties.ETag,
                    bodyLength = properties.Body?.Length ?? 0,
                });
                rows.Add((
                    properties.Id,
                    properties.TriggerType.ToString(),
                    properties.TriggerOperation.ToString(),
                    properties.Body?.Length ?? 0));
            }
        }

        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetString("command-trigger-list-empty")));
        }
        else
        {
            AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-trigger-list-title")));
            var table = new Table();
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-trigger-list-column-id"))));
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-trigger-list-column-type"))));
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-trigger-list-column-operation"))));
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-trigger-list-column-size"))).RightAligned());

            foreach (var row in rows)
            {
                table.AddRow(
                    Theme.FormatTableValue(row.Id),
                    Theme.FormatTableValue(row.Type),
                    Theme.FormatTableValue(row.Operation),
                    Theme.FormatTableValue(row.BodyLength.ToString()));
            }

            AnsiConsole.Write(table);
        }

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(items));
        commandState.IsPrinted = true;
        return commandState;
    }

    private async Task<CommandState> ShowAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();

        try
        {
            var response = await container.Scripts.ReadTriggerAsync(name, cancellationToken: token);
            commandState.Result = new ShellText(response.Resource.Body);
            return commandState;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw NotFound(name, ex);
        }
    }

    private async Task<CommandState> ExistsAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();

        bool exists;
        try
        {
            await container.Scripts.ReadTriggerAsync(name, cancellationToken: token);
            exists = true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            exists = false;
        }

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            exists ? "command-trigger-exists-yes" : "command-trigger-exists-no",
            "name",
            name));

        commandState.Result = new ShellBool(exists);
        commandState.IsPrinted = true;
        return commandState;
    }

    private async Task<CommandState> CreateAsync(Container container, ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();
        bool force = this.Force == true;

        if (string.IsNullOrWhiteSpace(this.Type))
        {
            throw new CommandException("trigger", MessageService.GetString("command-trigger-error-missing_type"));
        }

        var triggerType = ParseTriggerType(this.Type);
        var triggerOperation = ParseTriggerOperation(this.Operation);

        // When a body is supplied (a file or piped input), create directly. This is
        // the non-interactive path used by scripts.
        var explicitBody = this.TryReadExplicitBody(commandState);
        if (explicitBody is not null)
        {
            return await this.WriteCreateAsync(container, commandState, name, explicitBody, triggerType, triggerOperation, force, token);
        }

        // No body supplied: seed a default trigger and open the editor so an
        // interactive user can author it, then confirm before creating. Seeding needs a
        // real terminal, so scripts and MCP must pass a file instead.
        if (Console.IsInputRedirected || !string.IsNullOrEmpty(shell.CurrentScriptFileName))
        {
            throw new CommandException("trigger", MessageService.GetString("command-trigger-error-missing_file"));
        }

        // Check for an existing trigger before opening the editor so the user
        // is not asked to author a body that cannot be saved. When --force is set, seed
        // the editor with the existing body so it can be edited in place.
        string? existingBody;
        try
        {
            var read = await container.Scripts.ReadTriggerAsync(name, cancellationToken: token);
            existingBody = read.Resource.Body;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            existingBody = null;
        }

        if (existingBody is not null && !force)
        {
            throw new CommandException(
                "trigger",
                MessageService.GetArgsString("command-trigger-error-already_exists", "name", name));
        }

        var seed = existingBody ?? DefaultTriggerBody();
        var edited = await this.LaunchEditorAsync(seed, name, token);

        if (!string.IsNullOrWhiteSpace(edited))
        {
            AnsiConsole.Clear();
            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-trigger-create-preview", "name", name));
            ShellInterpreter.WriteLine();
            ShellInterpreter.WriteLine(edited);
            ShellInterpreter.WriteLine();
        }

        if (string.IsNullOrWhiteSpace(edited) || !ShellInterpreter.Confirm("command-trigger-create-confirm"))
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-trigger-create-discarded", "name", name));
            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = name, created = false }));
            commandState.IsPrinted = true;
            return commandState;
        }

        return await this.WriteCreateAsync(container, commandState, name, edited, triggerType, triggerOperation, force, token);
    }

    private async Task<CommandState> WriteCreateAsync(Container container, CommandState commandState, string name, string body, TriggerType triggerType, TriggerOperation triggerOperation, bool force, CancellationToken token)
    {
        var properties = new TriggerProperties
        {
            Id = name,
            Body = body,
            TriggerType = triggerType,
            TriggerOperation = triggerOperation,
        };

        TriggerResponse response;
        bool replaced;
        if (force)
        {
            try
            {
                response = await container.Scripts.ReplaceTriggerAsync(properties, cancellationToken: token);
                replaced = true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                response = await container.Scripts.CreateTriggerAsync(properties, cancellationToken: token);
                replaced = false;
            }
        }
        else
        {
            try
            {
                response = await container.Scripts.CreateTriggerAsync(properties, cancellationToken: token);
                replaced = false;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                throw new CommandException(
                    "trigger",
                    MessageService.GetArgsString("command-trigger-error-already_exists", "name", name),
                    ex);
            }
        }

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            replaced ? "command-trigger-replaced" : "command-trigger-created",
            "name",
            name,
            "charge",
            response.RequestCharge.ToString("F2")));

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new
        {
            id = name,
            triggerType = triggerType.ToString(),
            triggerOperation = triggerOperation.ToString(),
        }));
        commandState.IsPrinted = true;
        return commandState;
    }

    private async Task<CommandState> DeleteAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();

        try
        {
            var response = await container.Scripts.DeleteTriggerAsync(name, cancellationToken: token);
            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                "command-trigger-deleted",
                "name",
                name,
                "charge",
                response.RequestCharge.ToString("F2")));

            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = name, deleted = true }));
            commandState.IsPrinted = true;
            return commandState;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw NotFound(name, ex);
        }
    }

    private async Task<CommandState> EditAsync(Container container, ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();

        if (Console.IsInputRedirected || !string.IsNullOrEmpty(shell.CurrentScriptFileName))
        {
            throw new CommandException("trigger", MessageService.GetString("command-trigger-error-not_interactive"));
        }

        TriggerProperties existing;
        try
        {
            var read = await container.Scripts.ReadTriggerAsync(name, cancellationToken: token);
            existing = read.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw NotFound(name, ex);
        }

        var newBody = await this.LaunchEditorAsync(existing.Body, name, token);

        if (string.Equals(newBody, existing.Body, StringComparison.Ordinal))
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-trigger-edit-unchanged", "name", name));
            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = name, changed = false }));
            commandState.IsPrinted = true;
            return commandState;
        }

        var properties = new TriggerProperties
        {
            Id = name,
            Body = newBody,
            TriggerType = existing.TriggerType,
            TriggerOperation = existing.TriggerOperation,
        };
        var response = await container.Scripts.ReplaceTriggerAsync(properties, cancellationToken: token);

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            "command-trigger-replaced",
            "name",
            name,
            "charge",
            response.RequestCharge.ToString("F2")));

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = name, changed = true }));
        commandState.IsPrinted = true;
        return commandState;
    }

    /// <summary>
    /// Writes the supplied body to a temporary <c>.js</c> file, opens it in the
    /// external editor, waits for the editor to close, and returns the edited
    /// contents. The temporary file is always removed.
    /// </summary>
    private async Task<string> LaunchEditorAsync(string initialBody, string name, CancellationToken token)
    {
        var editor = ExternalEditor.Resolve(null);
        if (editor is null)
        {
            throw new CommandException("trigger", MessageService.GetString("command-trigger-error-no_editor"));
        }

        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"cosmos-trigger-{Guid.NewGuid():N}.js");

        try
        {
            await File.WriteAllTextAsync(tempPath, initialBody, token);

            AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetArgsString(
                "command-trigger-edit-launching",
                "name",
                name,
                "editor",
                editor.DisplayName)));

            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = editor.FileName,
                Arguments = editor.BuildArguments(tempPath),
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException("Process.Start returned null"))
            {
                var launched = System.Diagnostics.Stopwatch.StartNew();
                await process.WaitForExitAsync(token);
                launched.Stop();
                if (process.ExitCode != 0)
                {
                    throw new CommandException(
                        "trigger",
                        MessageService.GetArgsString("command-trigger-edit-exit-nonzero", "editor", editor.DisplayName, "code", process.ExitCode));
                }

                // Some editors (for example Windows notepad, or 'code' without
                // --wait) hand the file to a running instance and exit right
                // away instead of blocking until the window closes. When that
                // happens, wait for the user to confirm they finished editing
                // before reading the file back.
                if (launched.Elapsed < QuickEditorExit)
                {
                    ShellInterpreter.WriteLine(MessageService.GetString("command-trigger-edit-wait"));
                    Console.ReadLine();
                }
            }

            return await File.ReadAllTextAsync(tempPath, token);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of the temporary file.
            }
        }
    }

    private string RequireName()
    {
        if (string.IsNullOrWhiteSpace(this.Name))
        {
            throw new CommandException("trigger", MessageService.GetString("command-trigger-error-missing_name"));
        }

        return this.Name;
    }

    private string? TryReadExplicitBody(CommandState commandState)
    {
        if (!string.IsNullOrWhiteSpace(this.Value))
        {
            if (!File.Exists(this.Value))
            {
                throw new CommandException(
                    "trigger",
                    MessageService.GetArgsString("command-trigger-error-file_not_found", "file", this.Value));
            }

            return File.ReadAllText(this.Value);
        }

        var piped = commandState.Result?.ConvertShellObject(DataType.Text) as string;
        return string.IsNullOrEmpty(piped) ? null : piped;
    }
}
