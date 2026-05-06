// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Collections.Concurrent;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;

using RadLine;

using Spectre.Console;

internal sealed class CosmosCompleteCommand(ShellInterpreter shellInterpreter, AutoComplete kind) : LineEditorCommand
{
    private const string Position = nameof(Position);
    private const string Index = nameof(Index);
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromSeconds(30);

    private static readonly ConcurrentDictionary<string, CompletionCacheEntry> DatabaseCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, CompletionCacheEntry> ContainerCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, long> DatabaseRefreshTasks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, long> ContainerRefreshTasks = new(StringComparer.OrdinalIgnoreCase);
    private static long databaseCacheVersion;
    private static long containerCacheVersion;
    private static long refreshTaskVersion;

    private readonly ShellInterpreter shellInterpreter = shellInterpreter;
    private readonly AutoComplete kind = kind;

    public static void ClearDatabases()
    {
        Interlocked.Increment(ref databaseCacheVersion);
        DatabaseCache.Clear();
        DatabaseRefreshTasks.Clear();
    }

    public static void ClearContainers()
    {
        Interlocked.Increment(ref containerCacheVersion);
        ContainerCache.Clear();
        ContainerRefreshTasks.Clear();
    }

    public static void SetDatabases(CosmosClient client, IEnumerable<string> names)
    {
        var key = GetDatabaseCacheKey(client);
        Interlocked.Increment(ref databaseCacheVersion);
        DatabaseCache[key] = new CompletionCacheEntry([.. names], DateTimeOffset.UtcNow);
        DatabaseRefreshTasks.TryRemove(key, out _);
    }

    public static void SetContainers(CosmosClient client, string databaseName, IEnumerable<string> names)
    {
        var key = GetContainerCacheKey(client, databaseName);
        Interlocked.Increment(ref containerCacheVersion);
        ContainerCache[key] = new CompletionCacheEntry([.. names], DateTimeOffset.UtcNow);
        ContainerRefreshTasks.TryRemove(key, out _);
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
                            items.AddRange(GetDatabases(cs));
                        }
                    }

                    if ((parameter.ParameterType & ParameterType.Container) == ParameterType.Container)
                    {
                        if (shellInterpreter.State is DatabaseState cs)
                        {
                            items.AddRange(GetContainers(cs));
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

    private static string[] GetDatabases(ConnectedState state)
    {
        var key = GetDatabaseCacheKey(state.Client);
        var cached = DatabaseCache.GetValueOrDefault(key);
        var cacheVersion = Volatile.Read(ref databaseCacheVersion);
        QueueRefresh(DatabaseRefreshTasks, key, cached, () => RefreshDatabasesAsync(state, key, cacheVersion));
        return cached?.Items ?? [];
    }

    private static string[] GetContainers(DatabaseState state)
    {
        var key = GetContainerCacheKey(state.Client, state.DatabaseName);
        var cached = ContainerCache.GetValueOrDefault(key);
        var cacheVersion = Volatile.Read(ref containerCacheVersion);
        QueueRefresh(ContainerRefreshTasks, key, cached, () => RefreshContainersAsync(state, key, cacheVersion));
        return cached?.Items ?? [];
    }

    private static void QueueRefresh(
        ConcurrentDictionary<string, long> refreshTasks,
        string key,
        CompletionCacheEntry? cached,
        Func<Task> refresh)
    {
        if (cached != null && DateTimeOffset.UtcNow - cached.RefreshedAt < RefreshAfter)
        {
            return;
        }

        var registration = Interlocked.Increment(ref refreshTaskVersion);
        if (!refreshTasks.TryAdd(key, registration))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await refresh();
            }
            finally
            {
                RemoveRefreshTask(refreshTasks, key, registration);
            }
        });
    }

    private static void RemoveRefreshTask(ConcurrentDictionary<string, long> refreshTasks, string key, long registration)
    {
        ((ICollection<KeyValuePair<string, long>>)refreshTasks).Remove(new KeyValuePair<string, long>(key, registration));
    }

    private static async Task RefreshDatabasesAsync(ConnectedState state, string key, long cacheVersion)
    {
        try
        {
            var names = await GetDatabasesAsync(state);
            if (cacheVersion == Volatile.Read(ref databaseCacheVersion))
            {
                DatabaseCache[key] = new CompletionCacheEntry(names, DateTimeOffset.UtcNow);
            }
        }
        catch
        {
            return;
        }
    }

    private static async Task RefreshContainersAsync(DatabaseState state, string key, long cacheVersion)
    {
        try
        {
            var names = await GetContainersAsync(state);
            if (cacheVersion == Volatile.Read(ref containerCacheVersion))
            {
                ContainerCache[key] = new CompletionCacheEntry(names, DateTimeOffset.UtcNow);
            }
        }
        catch
        {
            return;
        }
    }

    private static string GetDatabaseCacheKey(CosmosClient client)
    {
        return client.Endpoint.ToString();
    }

    private static string GetContainerCacheKey(CosmosClient client, string databaseName)
    {
        return string.Join('|', client.Endpoint.ToString(), databaseName);
    }

    private static async Task<string[]> GetDatabasesAsync(ConnectedState state)
    {
        var result = new List<string>();
        await foreach (var name in CosmosResourceFacade.GetDatabaseNamesAsync(state, CancellationToken.None))
        {
            result.Add(name);
        }

        return [.. result];
    }

    private static async Task<string[]> GetContainersAsync(DatabaseState state)
    {
        var result = new List<string>();
        await foreach (var name in CosmosResourceFacade.GetContainerNamesAsync(state, state.DatabaseName, CancellationToken.None))
        {
            result.Add(name);
        }

        return [.. result];
    }

    private sealed record CompletionCacheEntry(string[] Items, DateTimeOffset RefreshedAt);
}
