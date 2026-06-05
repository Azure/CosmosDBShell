//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.IO;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Mcp;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using Spectre.Console;

/// <summary>
/// Opens a local file (for example a <c>.csh</c> script) in an external editor and
/// waits for the editor to close. The file is created when it does not yet exist.
/// </summary>
[CosmosCommand("edit")]
[CosmosExample("edit deploy.csh", Description = "Open deploy.csh in $EDITOR, creating it if needed")]
[McpAnnotation(Restricted = true)]
internal class EditCommand : CosmosCommand
{
    [CosmosParameter("path", IsRequired = true, ParameterType = ParameterType.File)]
    public string? FilePath { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(this.FilePath))
        {
            throw new CommandException("edit", MessageService.GetString("command-edit-missing-path"));
        }

        if (Console.IsInputRedirected || !string.IsNullOrEmpty(shell.CurrentScriptFileName))
        {
            var message = MessageService.GetString("command-edit-not-interactive");
            AnsiConsole.MarkupLine(Theme.FormatError(message));
            return Task.FromResult<CommandState>(new ErrorCommandState(new CommandException("edit", message)));
        }

        var path = this.FilePath;

        try
        {
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(path))
            {
                File.WriteAllText(path, string.Empty);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            var message = MessageService.GetArgsString("command-edit-create-failed", "path", path, "message", ex.Message);
            AnsiConsole.MarkupLine(Theme.FormatError(message));
            return Task.FromResult<CommandState>(new ErrorCommandState(new CommandException("edit", message, ex)));
        }

        var editor = ExternalEditor.Resolve(null);
        if (editor is null)
        {
            var message = MessageService.GetString("command-edit-no-editor");
            AnsiConsole.MarkupLine(Theme.FormatError(message));
            return Task.FromResult<CommandState>(new ErrorCommandState(new CommandException("edit", message)));
        }

        AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetArgsString(
            "command-edit-launching",
            "path",
            path,
            "editor",
            editor.DisplayName)));

        int exitCode;
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = editor.FileName,
                Arguments = editor.BuildArguments(path),
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException("Process.Start returned null");
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (Exception ex)
        {
            var message = MessageService.GetArgsString(
                "command-edit-launch-failed",
                "editor",
                editor.DisplayName,
                "path",
                path,
                "message",
                ex.Message);
            AnsiConsole.MarkupLine(Theme.FormatError(message));
            return Task.FromResult<CommandState>(new ErrorCommandState(new CommandException("edit", message, ex)));
        }

        if (exitCode != 0)
        {
            var message = MessageService.GetArgsString(
                "command-edit-exit-nonzero",
                "editor",
                editor.DisplayName,
                "code",
                exitCode);
            AnsiConsole.MarkupLine(Theme.FormatWarning(message));
            return Task.FromResult<CommandState>(new ErrorCommandState(new CommandException("edit", message)));
        }

        AnsiConsole.MarkupLine(Theme.FormatMuted(MessageService.GetArgsString("command-edit-saved", "path", path)));
        commandState.IsPrinted = true;
        commandState.Result = new ShellJson(JsonSerializer.SerializeToElement(new { edited = path }));
        return Task.FromResult(commandState);
    }
}
