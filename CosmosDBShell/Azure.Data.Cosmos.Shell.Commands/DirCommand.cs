//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;

using Spectre.Console;

/// <summary>
/// Lists files and directories in the local file system.
/// </summary>
[CosmosCommand("dir")]
[CosmosExample("dir", Description = "List files in the current directory")]
[CosmosExample("dir C:\\temp", Description = "List files in a specific directory")]
[CosmosExample("dir --directory=C:\\temp", Description = "List files using the directory option")]
[CosmosExample("dir *.json", Description = "List only JSON files in current directory")]
[CosmosExample("dir *.cs --directory=src", Description = "List C# files in the src directory")]
[CosmosExample("dir -r", Description = "List files recursively")]
[CosmosExample("dir -l", Description = "List file names only")]
internal class DirCommand : CosmosCommand
{
    [CosmosParameter("filter", IsRequired = false)]
    public string? Filter { get; init; }

    [CosmosOption("directory", "d")]
    public string? Directory { get; init; }

    [CosmosOption("recursive", "r")]
    public bool Recursive { get; init; }

    [CosmosOption("list", "l")]
    public bool ListOnly { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        var directory = this.Directory ?? System.IO.Directory.GetCurrentDirectory();
        var filter = this.Filter ?? "*";

        // Check if the filter is actually a directory path (when no --directory option is used)
        if (this.Directory == null && !string.IsNullOrEmpty(this.Filter) && System.IO.Directory.Exists(this.Filter))
        {
            directory = this.Filter;
            filter = "*";
        }

        if (!System.IO.Directory.Exists(directory))
        {
            throw new CommandException("dir", MessageService.GetArgsString("command-dir-directory_not_found", "directory", directory));
        }

        var searchOption = this.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var entries = new List<FileSystemEntry>();

        try
        {
            // Get directories
            foreach (var dir in System.IO.Directory.GetDirectories(directory, filter, searchOption))
            {
                var dirInfo = new DirectoryInfo(dir);
                entries.Add(new FileSystemEntry
                {
                    Name = dirInfo.Name,
                    FullPath = dirInfo.FullName,
                    IsDirectory = true,
                    Size = null,
                    LastModified = dirInfo.LastWriteTime,
                });
            }

            // Get files
            foreach (var file in System.IO.Directory.GetFiles(directory, filter, searchOption))
            {
                var fileInfo = new FileInfo(file);
                entries.Add(new FileSystemEntry
                {
                    Name = fileInfo.Name,
                    FullPath = fileInfo.FullName,
                    IsDirectory = false,
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime,
                });
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new CommandException("dir", MessageService.GetArgsString("command-dir-access_denied", "message", ex.Message));
        }
        catch (IOException ex)
        {
            throw new CommandException("dir", ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw new CommandException("dir", MessageService.GetArgsString("command-dir-invalid_filter", "message", ex.Message));
        }

        // Sort: directories first, then by name
        entries = entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var returnState = new CommandState();

        // Display results
        foreach (var entry in entries)
        {
            if (this.ListOnly)
            {
                // Simple list mode: just show the name
                if (entry.IsDirectory)
                {
                    AnsiConsole.MarkupLine($"[blue]{Markup.Escape(entry.Name)}/[/]");
                }
                else
                {
                    AnsiConsole.WriteLine(entry.Name);
                }
            }
            else
            {
                // Detailed mode: show date, size, and name
                if (entry.IsDirectory)
                {
                    AnsiConsole.MarkupLine($"[blue]{Markup.Escape(entry.Name)}/[/]");
                }
                else
                {
                    var sizeStr = FormatFileSize(entry.Size ?? 0);
                    AnsiConsole.MarkupLine($"[grey]{entry.LastModified:yyyy-MM-dd HH:mm}[/]  [white]{sizeStr,10}[/]  {Markup.Escape(entry.Name)}");
                }
            }
        }

        if (!this.ListOnly)
        {
            AnsiConsole.MarkupLine(MessageService.GetArgsString(
                "command-dir-summary",
                "fileCount",
                entries.Count(e => !e.IsDirectory),
                "dirCount",
                entries.Count(e => e.IsDirectory)));
        }

        // Set JSON result
        var jsonEntries = entries.Select(e => new
        {
            name = e.Name,
            path = e.FullPath,
            isDirectory = e.IsDirectory,
            size = e.Size,
            lastModified = e.LastModified,
        });

        returnState.Result = new ShellJson(JsonSerializer.SerializeToElement(jsonEntries));
        returnState.IsPrinted = true;

        return Task.FromResult(returnState);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    private class FileSystemEntry
    {
        public required string Name { get; init; }

        public required string FullPath { get; init; }

        public required bool IsDirectory { get; init; }

        public long? Size { get; init; }

        public DateTime LastModified { get; init; }
    }
}
