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

[CosmosCommand("sproc")]
[CosmosExample("sproc list", Description = "List the stored procedures in the current container")]
[CosmosExample("sproc show myProc", Description = "Display the body of a stored procedure")]
[CosmosExample("sproc exists myProc", Description = "Check whether a stored procedure exists (usable in if conditions)")]
[CosmosExample("sproc create myProc ./myProc.js", Description = "Create a stored procedure from a JavaScript file")]
[CosmosExample("sproc create myProc ./myProc.js --force", Description = "Create or replace a stored procedure")]
[CosmosExample("sproc edit myProc", Description = "Edit a stored procedure body in an external editor")]
[CosmosExample("sproc exec myProc '[\"param1\", \"param2\"]' --partition-key pk1", Description = "Execute a stored procedure with parameters")]
[CosmosExample("sproc delete myProc", Description = "Delete a stored procedure")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(
    Title = "Stored Procedures",
    Description = @"
Manages JavaScript stored procedures on the current Cosmos DB container through subcommands:
- 'list' returns the stored procedure ids in the container.
- 'show <name>' returns the body of a stored procedure.
- 'exists <name>' returns whether a stored procedure exists.
- 'create <name> <file>' creates a stored procedure from a JavaScript file. Pass --force to replace an existing one.
- 'exec <name> [params]' executes a stored procedure. 'params' is a JSON array of arguments and --partition-key selects the target partition.
- 'edit <name>' opens an existing stored procedure body in an external editor.
- 'delete <name>' removes a stored procedure.
This command is restricted in MCP. Run it manually in the shell.
",
    Restricted = true,
    Destructive = true,
    OpenWorld = true)]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class SprocCommand : CosmosCommand
{
    /// <summary>
    /// When the launched editor returns faster than this, assume it handed the
    /// file to a background instance (for example Windows notepad or 'code'
    /// without --wait) and prompt the user before reading the file back.
    /// </summary>
    private static readonly TimeSpan QuickEditorExit = TimeSpan.FromSeconds(2);

    [CosmosParameter("subcommand", RequiredErrorKey = "command-sproc-error-missing_subcommand")]
    public string Subcommand { get; init; } = string.Empty;

    [CosmosParameter("name", IsRequired = false)]
    public string? Name { get; init; }

    [CosmosParameter("value", IsRequired = false)]
    public string? Value { get; init; }

    [CosmosOption("partition-key", "pk")]
    public string? PartitionKey { get; init; }

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
    /// Parses the JSON array of stored procedure arguments. Returns an empty array
    /// when no value is supplied. Each argument is preserved as a <see cref="JsonElement"/>
    /// so the Cosmos SDK serializes it with the correct type.
    /// </summary>
    internal static object[] ParseExecParams(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new CommandException("sproc", MessageService.GetString("command-sproc-error-invalid_params"), ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new CommandException("sproc", MessageService.GetString("command-sproc-error-invalid_params"));
            }

            var parameters = new List<object>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                parameters.Add(element.Clone());
            }

            return [.. parameters];
        }
    }

    /// <summary>
    /// Parses the <c>--partition-key</c> value, preserving its JSON type when possible
    /// (including JSON arrays for hierarchical partition keys). Object-shaped or otherwise
    /// malformed JSON is rejected with a clear error rather than silently coerced.
    /// </summary>
    internal static PartitionKey ParsePartitionKey(string value)
    {
        try
        {
            return CreatePartitionKeyFromArgument(value);
        }
        catch (JsonException ex)
        {
            throw new CommandException("sproc", MessageService.GetString("command-sproc-error-invalid_pk"), ex);
        }
    }

    private static CommandException NotFound(string name, Exception inner) =>
        new("sproc", MessageService.GetArgsString("command-sproc-error-not_found", "name", name), inner);

    /// <summary>
    /// Returns a sample stored procedure body used to seed the editor when creating
    /// a new stored procedure without supplying a file. Mirrors the template offered
    /// by the Azure Databases extension for Visual Studio Code.
    /// </summary>
    internal static string DefaultStoredProcedureBody() =>
        """
        function sample(prefix) {
            var collection = getContext().getCollection();

            // Query documents and take 1st item.
            var isAccepted = collection.queryDocuments(
                collection.getSelfLink(),
                'SELECT * FROM root r',
                function (err, feed, options) {
                    if (err) throw err;

                    // Check the feed and if empty, set the body to 'no docs found',
                    // else take 1st element from feed.
                    if (!feed || !feed.length) {
                        var response = getContext().getResponse();
                        response.setBody('no docs found');
                    } else {
                        var response = getContext().getResponse();
                        var body = { prefix: prefix, feed: feed[0] };
                        response.setBody(JSON.stringify(body));
                    }
                });

            if (!isAccepted) throw new Error('The query was not accepted by the server.');
        }

        """;

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var subcommand = NormalizeSubcommand(this.Subcommand);
        if (subcommand.Length == 0)
        {
            throw new CommandException("sproc", MessageService.GetString("command-sproc-error-missing_subcommand"));
        }

        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("sproc");
        }

        var (_, _, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "sproc",
            token);

        return subcommand switch
        {
            "list" or "ls" => await this.ListAsync(container, shell, commandState, token),
            "show" or "cat" => await this.ShowAsync(container, commandState, token),
            "exists" => await this.ExistsAsync(container, commandState, token),
            "create" or "set" => await this.CreateAsync(container, shell, commandState, token),
            "exec" or "run" => await this.ExecAsync(container, commandState, token),
            "edit" => await this.EditAsync(container, shell, commandState, token),
            "delete" or "rm" => await this.DeleteAsync(container, commandState, token),
            _ => throw new CommandException(
                "sproc",
                MessageService.GetArgsString("command-sproc-error-invalid_subcommand", "subcommand", subcommand)),
        };
    }

    private async Task<CommandState> ListAsync(Container container, ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var items = new List<object>();
        var rows = new List<(string Id, string Modified, int BodyLength)>();
        using var iterator = container.Scripts.GetStoredProcedureQueryIterator<StoredProcedureProperties>();
        while (iterator.HasMoreResults)
        {
            foreach (var properties in await iterator.ReadNextAsync(token))
            {
                items.Add(new
                {
                    id = properties.Id,
                    lastModified = properties.LastModified,
                    etag = properties.ETag,
                    bodyLength = properties.Body?.Length ?? 0,
                });
                rows.Add((
                    properties.Id,
                    properties.LastModified?.ToString("u") ?? string.Empty,
                    properties.Body?.Length ?? 0));
            }
        }

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(items));

        // When output is redirected, let the interpreter emit the JSON result so
        // `sproc list > out.json` honors the documented JSON contract instead of
        // writing a console table. Interactive sessions still get the table.
        if (!string.IsNullOrEmpty(shell.StdOutRedirect))
        {
            return commandState;
        }

        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetString("command-sproc-list-empty")));
        }
        else
        {
            AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-sproc-list-title")));
            var table = new Table();
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-sproc-list-column-id"))));
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-sproc-list-column-modified"))));
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-sproc-list-column-size"))).RightAligned());

            foreach (var row in rows)
            {
                table.AddRow(
                    Theme.FormatTableValue(row.Id),
                    Theme.FormatTableValue(row.Modified),
                    Theme.FormatTableValue(row.BodyLength.ToString()));
            }

            AnsiConsole.Write(table);
        }

        commandState.IsPrinted = true;
        return commandState;
    }

    private async Task<CommandState> ShowAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();

        try
        {
            var response = await container.Scripts.ReadStoredProcedureAsync(name, cancellationToken: token);
            commandState.Result = new ShellText(response.Resource.Body ?? string.Empty) { Highlighter = JavaScriptOutputHighlighter.BuildMarkup };
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
            await container.Scripts.ReadStoredProcedureAsync(name, cancellationToken: token);
            exists = true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            exists = false;
        }

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            exists ? "command-sproc-exists-yes" : "command-sproc-exists-no",
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

        // When a body is supplied (a file or piped input), create directly. This is
        // the non-interactive path used by scripts.
        var explicitBody = this.TryReadExplicitBody(commandState);
        if (explicitBody is not null)
        {
            return await this.WriteCreateAsync(container, commandState, name, explicitBody, force, token);
        }

        // No body supplied: seed a default stored procedure and open the editor so an
        // interactive user can author it, then confirm before creating. Seeding needs a
        // real terminal, so scripts and MCP must pass a file instead.
        if (Console.IsInputRedirected || !string.IsNullOrEmpty(shell.CurrentScriptFileName))
        {
            throw new CommandException("sproc", MessageService.GetString("command-sproc-error-missing_file"));
        }

        // Check for an existing stored procedure before opening the editor so the user
        // is not asked to author a body that cannot be saved. When --force is set, seed
        // the editor with the existing body so it can be edited in place.
        string? existingBody = null;
        try
        {
            var read = await container.Scripts.ReadStoredProcedureAsync(name, cancellationToken: token);
            existingBody = read.Resource.Body;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            existingBody = null;
        }

        if (existingBody is not null && !force)
        {
            throw new CommandException(
                "sproc",
                MessageService.GetArgsString("command-sproc-error-already_exists", "name", name));
        }

        var seed = existingBody ?? DefaultStoredProcedureBody();
        var edited = await this.LaunchEditorAsync(seed, name, token);

        if (!string.IsNullOrWhiteSpace(edited))
        {
            AnsiConsole.Clear();
            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-sproc-create-preview", "name", name));
            ShellInterpreter.WriteLine();
            ShellInterpreter.WriteLine(edited);
            ShellInterpreter.WriteLine();
        }

        if (string.IsNullOrWhiteSpace(edited) || !ShellInterpreter.Confirm("command-sproc-create-confirm"))
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-sproc-create-discarded", "name", name));
            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = name, created = false }));
            commandState.IsPrinted = true;
            return commandState;
        }

        return await this.WriteCreateAsync(container, commandState, name, edited, force, token);
    }

    private async Task<CommandState> WriteCreateAsync(Container container, CommandState commandState, string name, string body, bool force, CancellationToken token)
    {
        var properties = new StoredProcedureProperties { Id = name, Body = body };

        StoredProcedureResponse response;
        bool replaced;
        if (force)
        {
            try
            {
                response = await container.Scripts.ReplaceStoredProcedureAsync(properties, cancellationToken: token);
                replaced = true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                response = await container.Scripts.CreateStoredProcedureAsync(properties, cancellationToken: token);
                replaced = false;
            }
        }
        else
        {
            try
            {
                response = await container.Scripts.CreateStoredProcedureAsync(properties, cancellationToken: token);
                replaced = false;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                throw new CommandException(
                    "sproc",
                    MessageService.GetArgsString("command-sproc-error-already_exists", "name", name),
                    ex);
            }
        }

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            replaced ? "command-sproc-replaced" : "command-sproc-created",
            "name",
            name,
            "charge",
            response.RequestCharge.ToString("F2")));

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = name }));
        commandState.IsPrinted = true;
        return commandState;
    }

    private async Task<CommandState> ExecAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();

        if (string.IsNullOrWhiteSpace(this.PartitionKey))
        {
            throw new CommandException("sproc", MessageService.GetString("command-sproc-error-missing_partition_key"));
        }

        var parameters = ParseExecParams(this.Value);
        var partitionKey = ParsePartitionKey(this.PartitionKey);

        try
        {
            var response = await container.Scripts.ExecuteStoredProcedureAsync<JsonElement>(
                name,
                partitionKey,
                parameters,
                cancellationToken: token);

            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                "command-sproc-executed",
                "name",
                name,
                "charge",
                response.RequestCharge.ToString("F2")));

            commandState.Result = new ShellJson(response.Resource.Clone());
            return commandState;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw NotFound(name, ex);
        }
    }

    private async Task<CommandState> DeleteAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();

        try
        {
            var response = await container.Scripts.DeleteStoredProcedureAsync(name, cancellationToken: token);
            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                "command-sproc-deleted",
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
            throw new CommandException("sproc", MessageService.GetString("command-sproc-error-not_interactive"));
        }

        string existingBody;
        try
        {
            var read = await container.Scripts.ReadStoredProcedureAsync(name, cancellationToken: token);
            existingBody = read.Resource.Body;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw NotFound(name, ex);
        }

        var newBody = await this.LaunchEditorAsync(existingBody, name, token);

        if (string.Equals(newBody, existingBody, StringComparison.Ordinal))
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-sproc-edit-unchanged", "name", name));
            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = name, changed = false }));
            commandState.IsPrinted = true;
            return commandState;
        }

        var properties = new StoredProcedureProperties { Id = name, Body = newBody };
        var response = await container.Scripts.ReplaceStoredProcedureAsync(properties, cancellationToken: token);

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            "command-sproc-replaced",
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
            throw new CommandException("sproc", MessageService.GetString("command-sproc-error-no_editor"));
        }

        var tempPath = System.IO.Path.Join(
            System.IO.Path.GetTempPath(),
            $"cosmos-sproc-{Guid.NewGuid():N}.js");

        try
        {
            await File.WriteAllTextAsync(tempPath, initialBody, token);

            AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetArgsString(
                "command-sproc-edit-launching",
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
                        "sproc",
                        MessageService.GetArgsString("command-sproc-edit-exit-nonzero", "editor", editor.DisplayName, "code", process.ExitCode));
                }

                // Some editors (for example Windows notepad, or 'code' without
                // --wait) hand the file to a running instance and exit right
                // away instead of blocking until the window closes. When that
                // happens, wait for the user to confirm they finished editing
                // before reading the file back.
                if (launched.Elapsed < QuickEditorExit)
                {
                    ShellInterpreter.WriteLine(MessageService.GetString("command-sproc-edit-wait"));
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
            throw new CommandException("sproc", MessageService.GetString("command-sproc-error-missing_name"));
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
                    "sproc",
                    MessageService.GetArgsString("command-sproc-error-file_not_found", "file", this.Value));
            }

            return File.ReadAllText(this.Value);
        }

        var piped = commandState.Result?.ConvertShellObject(DataType.Text) as string;
        return string.IsNullOrEmpty(piped) ? null : piped;
    }
}
