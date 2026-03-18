// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;

using RadLine;

using Spectre.Console;

internal sealed class CosmosCompleteCommand(ShellInterpreter shellInterpreter, AutoComplete kind) : LineEditorCommand
{
    private const string Position = nameof(Position);
    private const string Index = nameof(Index);

    private static string[]? databases = null;
    private static string[]? containers = null;

    private readonly ShellInterpreter shellInterpreter = shellInterpreter;
    private readonly AutoComplete kind = kind;

    public static void ClearDatabases()
    {
        databases = null;
    }

    public static void ClearContainers()
    {
        containers = null;
    }

    public static string? GetCompletion(ShellInterpreter shellInterpreter, string word, AutoComplete kind)
    {
        if (word == shellInterpreter.LastBuffer)
        {
            word = shellInterpreter.OriginalString ?? string.Empty;
        }
        else
        {
            shellInterpreter.OriginalString = word;
            shellInterpreter.LastBuffer = null;
        }

        try
        {
            var commandLine = new StatementParser(word).ParseStatement() as CommandStatement;
            if (commandLine == null || commandLine.Arguments.Count == 0)
            {
                return CompleteCommand(shellInterpreter.App.Commands.Keys, word, shellInterpreter.LastBuffer ?? string.Empty, kind);
            }

            if (shellInterpreter.App.Commands.TryGetValue(commandLine.Name, out var cmd))
            {
                if (commandLine.Arguments.Count <= cmd.Parameters.Count)
                {
                    var parameter = cmd.Parameters[commandLine.Arguments.Count - 1];
                    var curArg = commandLine.Arguments.Last();
                    var txt = curArg.ToString();
                    var items = new List<string>();
                    if ((parameter.ParameterType & ParameterType.File) == ParameterType.File)
                    {
                        items.AddRange(Directory.GetFiles(".").Select(f =>
                        {
                            return Path.GetFileName(f) ?? string.Empty;
                        }));
                    }

                    if ((parameter.ParameterType & ParameterType.Database) == ParameterType.Database)
                    {
                        if (shellInterpreter.State is ConnectedState cs)
                        {
#pragma warning disable VSTHRD002 // Synchronously waiting - required by RadLine LineEditorCommand.Execute
                            databases ??= GetDatabasesAsync(cs).Result;
#pragma warning restore VSTHRD002
                            if (databases != null)
                            {
                                items.AddRange(databases);
                            }
                        }
                    }

                    if ((parameter.ParameterType & ParameterType.Container) == ParameterType.Container)
                    {
                        if (shellInterpreter.State is DatabaseState cs)
                        {
#pragma warning disable VSTHRD002 // Synchronously waiting - required by RadLine LineEditorCommand.Execute
                            containers ??= GetContainersAsync(cs).Result;
#pragma warning restore VSTHRD002
                            if (containers != null)
                            {
                                items.AddRange(containers);
                            }
                        }
                    }

                    var cc = CompleteCommand(items, txt ?? string.Empty, shellInterpreter.LastBuffer ?? string.Empty, kind);
                    if (string.IsNullOrEmpty(cc))
                    {
                        return null;
                    }

                    return string.Concat(word.AsSpan(0, curArg.Start), cc);
                }
            }

            return null;
        }
        catch
        {
            // Exceptions during completion are ignored
            return null;
        }
    }

    public override void Execute(LineEditorContext context)
    {
        var word = context.Buffer.Content;
        var completion = GetCompletion(this.shellInterpreter, word, this.kind);

        if (completion != null)
        {
            this.shellInterpreter.LastBuffer = completion;
            context.Buffer.Clear(0, context.Buffer.Length);
            context.Buffer.Move(0);
            context.Buffer.Insert(completion);
            context.Buffer.MoveEnd();
        }
    }

    private static string? CompleteCommand(IEnumerable<string> items, string word, string lastBuffer, AutoComplete kind)
    {
        var foundLastWord = word.Length == 0;

        var matchingItems = items.Where(w => w.StartsWith(word)).ToList();
        for (var i = 0; i < matchingItems.Count; i++)
        {
            if (lastBuffer.EndsWith(matchingItems[i]))
            {
                if (kind == AutoComplete.Next)
                {
                    return matchingItems[(i + 1) % matchingItems.Count];
                }
                else
                {
                    return matchingItems[(i + matchingItems.Count - 1) % matchingItems.Count];
                }
            }
        }

        return matchingItems.FirstOrDefault();
    }

    private static async Task<string[]> GetDatabasesAsync(ConnectedState state)
    {
        var result = new List<string>();
        await foreach (var database in EnumerateDatabasesAsync(state.Client))
        {
            result.Add(database.Id);
        }

        return [.. result];
    }

    private static async Task<string[]> GetContainersAsync(DatabaseState state)
    {
        var result = new List<string>();
        await foreach (var container in EnumerateContainersAsync(state.Client.GetDatabase(state.DatabaseName)))
        {
            result.Add(container.Id);
        }

        return [.. result];
    }

    private static async IAsyncEnumerable<DatabaseProperties> EnumerateDatabasesAsync(CosmosClient client)
    {
        using var feedIterator = client.GetDatabaseQueryIterator<DatabaseProperties>("SELECT * FROM c");
        await foreach (var item in EnumerateFeedAsync(feedIterator))
        {
            yield return item;
        }
    }

    private static async IAsyncEnumerable<ContainerProperties> EnumerateContainersAsync(Database database)
    {
        using var feedIterator = database.GetContainerQueryIterator<ContainerProperties>("SELECT * FROM c");
        await foreach (var item in EnumerateFeedAsync(feedIterator))
        {
            yield return item;
        }
    }

    private static async IAsyncEnumerable<T> EnumerateFeedAsync<T>(FeedIterator<T> feedIterator)
    {
        while (feedIterator.HasMoreResults)
        {
            var response = await feedIterator.ReadNextAsync();
            foreach (var container in response)
            {
                yield return container;
            }
        }
    }
}
