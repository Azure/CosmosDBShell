// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;

using Xunit;

public class EndToEndScriptTests : ConnectedEmulatorTestBase
{
    [Fact]
    public async Task Script_CreateDbContainerAndInsertItems()
    {
        var dbName = $"E2E_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        var script = $"""
            mkdb {dbName}
            cd {dbName}
            mkcon Items /id
            cd Items
            mkitem '{JsonSerializer.Serialize(new { id = "item1", name = "first" })}'
            mkitem '{JsonSerializer.Serialize(new { id = "item2", name = "second" })}'
            mkitem '{JsonSerializer.Serialize(new { id = "item3", name = "third" })}'
            ls
            """;

        var state = await ExecuteAsync(script);
        Assert.False(state.IsError, FormatError(state));
    }

    [Fact]
    public async Task Script_NavigateAndQuery()
    {
        var dbName = $"E2E_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        var setupScript = $"""
            mkdb {dbName}
            cd {dbName}
            mkcon QueryTest /id
            cd QueryTest
            mkitem '{JsonSerializer.Serialize(new { id = "q1", val = 10 })}'
            mkitem '{JsonSerializer.Serialize(new { id = "q2", val = 20 })}'
            """;

        var setupState = await ExecuteAsync(setupScript);
        Assert.False(setupState.IsError, FormatError(setupState));

        var queryState = await ExecuteAsync("query \"SELECT * FROM c WHERE c.val > 5\"");
        Assert.False(queryState.IsError, FormatError(queryState));
    }
}
