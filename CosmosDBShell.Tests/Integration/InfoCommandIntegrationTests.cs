// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Threading.Tasks;

using Xunit;

/// <summary>
/// Emulator integration tests for the <c>info</c> command. These verify that the
/// command runs without error against a real container, database, and account,
/// including the partition-distribution and detailed scan paths.
/// </summary>
public class InfoCommandIntegrationTests : ConnectedEmulatorTestBase
{
    [Fact]
    public async Task Info_Container_ReportsWithoutError()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon Items /id");
        await ExecuteAsync("cd Items");
        await ExecuteAsync("mkitem \"{ \\\"id\\\": \\\"a\\\" }\"");
        await ExecuteAsync("mkitem \"{ \\\"id\\\": \\\"b\\\" }\"");

        var state = await ExecuteAsync("info");
        Assert.False(state.IsError, FormatError(state));
        Assert.NotNull(state.Result);
    }

    [Fact]
    public async Task Info_Container_Partitions_ReportsWithoutError()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon Items /id");
        await ExecuteAsync("cd Items");
        await ExecuteAsync("mkitem \"{ \\\"id\\\": \\\"a\\\" }\"");

        var state = await ExecuteAsync("info --partitions");
        Assert.False(state.IsError, FormatError(state));
        Assert.NotNull(state.Result);
    }

    [Fact]
    public async Task Info_Container_Detailed_ReportsWithoutError()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon Items /id");
        await ExecuteAsync("cd Items");
        await ExecuteAsync("mkitem \"{ \\\"id\\\": \\\"a\\\" }\"");
        await ExecuteAsync("mkitem \"{ \\\"id\\\": \\\"b\\\" }\"");

        var state = await ExecuteAsync("info --detailed");
        Assert.False(state.IsError, FormatError(state));
        Assert.NotNull(state.Result);
    }

    [Fact]
    public async Task Info_Database_ReportsWithoutError()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon Items /id");

        var state = await ExecuteAsync($"info --database={dbName}");
        Assert.False(state.IsError, FormatError(state));
        Assert.NotNull(state.Result);
    }

    [Fact]
    public async Task Info_Account_ReportsWithoutError()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");

        var state = await ExecuteAsync("info");
        Assert.False(state.IsError, FormatError(state));
        Assert.NotNull(state.Result);
    }

    [Fact]
    public async Task Info_Account_Detailed_ReportsWithoutError()
    {
        var dbName = $"InfoTest_{Guid.NewGuid():N}";
        CreatedDatabases.Add(dbName);

        await ExecuteAsync($"mkdb {dbName}");
        await ExecuteAsync($"cd {dbName}");
        await ExecuteAsync("mkcon Items /id");
        await ExecuteAsync("cd ..");

        var state = await ExecuteAsync("info --detailed");
        Assert.False(state.IsError, FormatError(state));
        Assert.NotNull(state.Result);
    }
}
