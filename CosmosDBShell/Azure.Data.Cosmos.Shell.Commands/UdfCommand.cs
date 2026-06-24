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

[CosmosCommand("udf")]
[CosmosExample("udf list", Description = "List the user-defined functions in the current container")]
[CosmosExample("udf show myFunc", Description = "Display the body of a user-defined function")]
[CosmosExample("udf exists myFunc", Description = "Check whether a user-defined function exists (usable in if conditions)")]
[CosmosExample("udf create myFunc ./myFunc.js", Description = "Create a user-defined function from a JavaScript file")]
[CosmosExample("udf create myFunc ./myFunc.js --force", Description = "Create or replace a user-defined function")]
[CosmosExample("udf edit myFunc", Description = "Edit a user-defined function body in an external editor")]
[CosmosExample("udf delete myFunc", Description = "Delete a user-defined function")]
#pragma warning disable SA1118 // Parameter should not span multiple lines
[McpAnnotation(
    Title = "User-Defined Functions",
    Description = @"
Manages JavaScript user-defined functions (UDFs) on the current Cosmos DB container through subcommands:
- 'list' returns the user-defined function ids in the container.
- 'show <name>' returns the body of a user-defined function.
- 'exists <name>' returns whether a user-defined function exists.
- 'create <name> <file>' creates a user-defined function from a JavaScript file. Pass --force to replace an existing one.
- 'edit <name>' opens an existing user-defined function body in an external editor.
- 'delete <name>' removes a user-defined function.
This command is restricted in MCP. Run it manually in the shell.
",
    Restricted = true,
    Destructive = true,
    OpenWorld = true)]
#pragma warning restore SA1118 // Parameter should not span multiple lines
internal class UdfCommand : CosmosCommand
{
    /// <summary>
    /// When the launched editor returns faster than this, assume it handed the
    /// file to a background instance (for example Windows notepad or 'code'
    /// without --wait) and prompt the user before reading the file back.
    /// </summary>
    private static readonly TimeSpan QuickEditorExit = TimeSpan.FromSeconds(2);

    [CosmosParameter("subcommand", RequiredErrorKey = "command-udf-error-missing_subcommand")]
    public string Subcommand { get; init; } = string.Empty;

    [CosmosParameter("name", IsRequired = false)]
    public string? Name { get; init; }

    [CosmosParameter("value", IsRequired = false)]
    public string? Value { get; init; }

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
    /// Returns a sample user-defined function body used to seed the editor when
    /// creating a new function without supplying a file. Mirrors the template
    /// offered by the Azure Databases extension for Visual Studio Code.
    /// </summary>
    internal static string DefaultUserDefinedFunctionBody() =>
        """
        function tax(income) {
            if (income == undefined) {
                throw 'no input';
            }

            if (income < 1000) {
                return income * 0.1;
            } else if (income < 10000) {
                return income * 0.2;
            } else {
                return income * 0.4;
            }
        }

        """;

    public async override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var subcommand = NormalizeSubcommand(this.Subcommand);
        if (subcommand.Length == 0)
        {
            throw new CommandException("udf", MessageService.GetString("command-udf-error-missing_subcommand"));
        }

        if (shell.State is not ConnectedState connectedState)
        {
            throw new NotConnectedException("udf");
        }

        var (_, _, container) = await ResolveContainerAsync(
            connectedState.Client,
            shell.State,
            this.Database,
            this.Container,
            "udf",
            token);

        return subcommand switch
        {
            "list" or "ls" => await this.ListAsync(container, shell, commandState, token),
            "show" or "cat" => await this.ShowAsync(container, commandState, token),
            "exists" => await this.ExistsAsync(container, commandState, token),
            "create" or "set" => await this.CreateAsync(container, shell, commandState, token),
            "edit" => await this.EditAsync(container, shell, commandState, token),
            "delete" or "rm" => await this.DeleteAsync(container, commandState, token),
            _ => throw new CommandException(
                "udf",
                MessageService.GetArgsString("command-udf-error-invalid_subcommand", "subcommand", subcommand)),
        };
    }

    private static CommandException NotFound(string name, Exception inner) =>
        new("udf", MessageService.GetArgsString("command-udf-error-not_found", "name", name), inner);

    private async Task<CommandState> ListAsync(Container container, ShellInterpreter shell, CommandState commandState, CancellationToken token)
    {
        var items = new List<object>();
        var rows = new List<(string Id, int BodyLength)>();
        using var iterator = container.Scripts.GetUserDefinedFunctionQueryIterator<UserDefinedFunctionProperties>();
        while (iterator.HasMoreResults)
        {
            foreach (var properties in await iterator.ReadNextAsync(token))
            {
                items.Add(new
                {
                    id = properties.Id,
                    etag = properties.ETag,
                    bodyLength = properties.Body?.Length ?? 0,
                });
                rows.Add((
                    properties.Id,
                    properties.Body?.Length ?? 0));
            }
        }

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(items));

        // When output is redirected, let the interpreter emit the JSON result so
        // `udf list > out.json` honors the documented JSON contract instead of
        // writing a console table. Interactive sessions still get the table.
        if (!string.IsNullOrEmpty(shell.StdOutRedirect))
        {
            return commandState;
        }

        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetString("command-udf-list-empty")));
        }
        else
        {
            AnsiConsole.MarkupLine(Theme.FormatSectionHeader(MessageService.GetString("command-udf-list-title")));
            var table = new Table();
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-udf-list-column-id"))));
            table.AddColumn(new TableColumn(Theme.FormatSectionHeader(MessageService.GetString("command-udf-list-column-size"))).RightAligned());

            foreach (var row in rows)
            {
                table.AddRow(
                    Theme.FormatTableValue(row.Id),
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
            var response = await container.Scripts.ReadUserDefinedFunctionAsync(name, cancellationToken: token);
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
            await container.Scripts.ReadUserDefinedFunctionAsync(name, cancellationToken: token);
            exists = true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            exists = false;
        }

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            exists ? "command-udf-exists-yes" : "command-udf-exists-no",
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

        // No body supplied: seed a default function and open the editor so an
        // interactive user can author it, then confirm before creating. Seeding needs a
        // real terminal, so scripts and MCP must pass a file instead.
        if (Console.IsInputRedirected || !string.IsNullOrEmpty(shell.CurrentScriptFileName))
        {
            throw new CommandException("udf", MessageService.GetString("command-udf-error-missing_file"));
        }

        // Check for an existing function before opening the editor so the user
        // is not asked to author a body that cannot be saved. When --force is set, seed
        // the editor with the existing body so it can be edited in place.
        string? existingBody;
        try
        {
            var read = await container.Scripts.ReadUserDefinedFunctionAsync(name, cancellationToken: token);
            existingBody = read.Resource.Body;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            existingBody = null;
        }

        if (existingBody is not null && !force)
        {
            throw new CommandException(
                "udf",
                MessageService.GetArgsString("command-udf-error-already_exists", "name", name));
        }

        var seed = existingBody ?? DefaultUserDefinedFunctionBody();
        var edited = await this.LaunchEditorAsync(seed, name, token);

        if (!string.IsNullOrWhiteSpace(edited))
        {
            AnsiConsole.Clear();
            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-udf-create-preview", "name", name));
            ShellInterpreter.WriteLine();
            ShellInterpreter.WriteLine(edited);
            ShellInterpreter.WriteLine();
        }

        if (string.IsNullOrWhiteSpace(edited) || !ShellInterpreter.Confirm("command-udf-create-confirm"))
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-udf-create-discarded", "name", name));
            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = name, created = false }));
            commandState.IsPrinted = true;
            return commandState;
        }

        return await this.WriteCreateAsync(container, commandState, name, edited, force, token);
    }

    private async Task<CommandState> WriteCreateAsync(Container container, CommandState commandState, string name, string body, bool force, CancellationToken token)
    {
        var properties = new UserDefinedFunctionProperties { Id = name, Body = body };

        UserDefinedFunctionResponse response;
        bool replaced;
        if (force)
        {
            try
            {
                response = await container.Scripts.ReplaceUserDefinedFunctionAsync(properties, cancellationToken: token);
                replaced = true;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                response = await container.Scripts.CreateUserDefinedFunctionAsync(properties, cancellationToken: token);
                replaced = false;
            }
        }
        else
        {
            try
            {
                response = await container.Scripts.CreateUserDefinedFunctionAsync(properties, cancellationToken: token);
                replaced = false;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                throw new CommandException(
                    "udf",
                    MessageService.GetArgsString("command-udf-error-already_exists", "name", name),
                    ex);
            }
        }

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            replaced ? "command-udf-replaced" : "command-udf-created",
            "name",
            name,
            "charge",
            response.RequestCharge.ToString("F2")));

        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = name }));
        commandState.IsPrinted = true;
        return commandState;
    }

    private async Task<CommandState> DeleteAsync(Container container, CommandState commandState, CancellationToken token)
    {
        var name = this.RequireName();

        try
        {
            var response = await container.Scripts.DeleteUserDefinedFunctionAsync(name, cancellationToken: token);
            ShellInterpreter.WriteLine(MessageService.GetArgsString(
                "command-udf-deleted",
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
            throw new CommandException("udf", MessageService.GetString("command-udf-error-not_interactive"));
        }

        string existingBody;
        try
        {
            var read = await container.Scripts.ReadUserDefinedFunctionAsync(name, cancellationToken: token);
            existingBody = read.Resource.Body;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw NotFound(name, ex);
        }

        var newBody = await this.LaunchEditorAsync(existingBody, name, token);

        if (string.Equals(newBody, existingBody, StringComparison.Ordinal))
        {
            ShellInterpreter.WriteLine(MessageService.GetArgsString("command-udf-edit-unchanged", "name", name));
            commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = name, changed = false }));
            commandState.IsPrinted = true;
            return commandState;
        }

        var properties = new UserDefinedFunctionProperties { Id = name, Body = newBody };
        var response = await container.Scripts.ReplaceUserDefinedFunctionAsync(properties, cancellationToken: token);

        ShellInterpreter.WriteLine(MessageService.GetArgsString(
            "command-udf-replaced",
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
            throw new CommandException("udf", MessageService.GetString("command-udf-error-no_editor"));
        }

        var tempPath = System.IO.Path.Join(
            System.IO.Path.GetTempPath(),
            $"cosmos-udf-{Guid.NewGuid():N}.js");

        try
        {
            await File.WriteAllTextAsync(tempPath, initialBody, token);

            AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetArgsString(
                "command-udf-edit-launching",
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
                        "udf",
                        MessageService.GetArgsString("command-udf-edit-exit-nonzero", "editor", editor.DisplayName, "code", process.ExitCode));
                }

                // Some editors (for example Windows notepad, or 'code' without
                // --wait) hand the file to a running instance and exit right
                // away instead of blocking until the window closes. When that
                // happens, wait for the user to confirm they finished editing
                // before reading the file back.
                if (launched.Elapsed < QuickEditorExit)
                {
                    ShellInterpreter.WriteLine(MessageService.GetString("command-udf-edit-wait"));
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
            throw new CommandException("udf", MessageService.GetString("command-udf-error-missing_name"));
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
                    "udf",
                    MessageService.GetArgsString("command-udf-error-file_not_found", "file", this.Value));
            }

            return File.ReadAllText(this.Value);
        }

        var piped = commandState.Result?.ConvertShellObject(DataType.Text) as string;
        return string.IsNullOrEmpty(piped) ? null : piped;
    }
}
