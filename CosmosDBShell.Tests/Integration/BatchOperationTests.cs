// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;

using Xunit;

public class BatchOperationTests : EmulatorFixtureTestBase
{
    public BatchOperationTests(EmulatorDatabaseFixture fixture)
        : base(fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        await ExecuteAsync("cd");
        await ExecuteAsync($"cd {Fixture.DatabaseName}");
    }

    [Fact]
    public async Task BatchRun_MultipleCreates_CommitsAtomically()
    {
        await CreateBatchContainerAsync();
        const string pk = "tenant-success";
        var json = "[" +
            "{\"op\":\"create\",\"item\":{\"id\":\"a\",\"pk\":\"" + pk + "\"}}," +
            "{\"op\":\"create\",\"item\":{\"id\":\"b\",\"pk\":\"" + pk + "\"}}]";

        var output = await ExecuteWithOutputAsync($"batch run '{json}' --partition-key {pk}");
        var root = JsonDocument.Parse(output).RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(2, root.GetProperty("operationCount").GetInt32());

        var itemA = JsonDocument.Parse(await ExecuteWithOutputAsync($"print a {pk}")).RootElement;
        Assert.Equal("a", itemA.GetProperty("id").GetString());
        var itemB = JsonDocument.Parse(await ExecuteWithOutputAsync($"print b {pk}")).RootElement;
        Assert.Equal("b", itemB.GetProperty("id").GetString());
    }

    [Fact]
    public async Task BatchRun_CreateThenPatch_AppliesBoth()
    {
        await CreateBatchContainerAsync();
        const string pk = "tenant-patch";
        var json = "[" +
            "{\"op\":\"create\",\"item\":{\"id\":\"c1\",\"pk\":\"" + pk + "\",\"status\":\"new\"}}," +
            "{\"op\":\"patch\",\"id\":\"c1\",\"operations\":[{\"op\":\"set\",\"path\":\"/status\",\"value\":\"done\"}]}]";

        var output = await ExecuteWithOutputAsync($"batch run '{json}' --partition-key {pk}");
        Assert.True(JsonDocument.Parse(output).RootElement.GetProperty("success").GetBoolean());

        var item = JsonDocument.Parse(await ExecuteWithOutputAsync($"print c1 {pk}")).RootElement;
        Assert.Equal("done", item.GetProperty("status").GetString());
    }

    [Fact]
    public async Task BatchRun_FailingOperation_RollsBackEntireBatch()
    {
        await CreateBatchContainerAsync();
        const string pk = "tenant-rollback";

        // Seed an existing item so the second create conflicts.
        await ExecuteAsync($"mkitem '{{\"id\":\"existing\",\"pk\":\"{pk}\"}}'");

        var json = "[" +
            "{\"op\":\"create\",\"item\":{\"id\":\"fresh\",\"pk\":\"" + pk + "\"}}," +
            "{\"op\":\"create\",\"item\":{\"id\":\"existing\",\"pk\":\"" + pk + "\"}}]";

        var output = await ExecuteWithOutputAsync($"batch run '{json}' --partition-key {pk}");
        var root = JsonDocument.Parse(output).RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());

        // The first operation must have been rolled back: 'fresh' should not exist.
        var printState = await ExecuteAsync($"print fresh {pk}");
        Assert.True(printState.IsError);
    }

    [Fact]
    public async Task StatefulBatch_BeginAddExecute_CommitsQueuedOperations()
    {
        await CreateBatchContainerAsync();
        const string pk = "tenant-stateful";

        await ExecuteAsync($"batch begin --partition-key {pk}");
        await ExecuteAsync($"batch add '{{\"op\":\"create\",\"item\":{{\"id\":\"s1\",\"pk\":\"{pk}\"}}}}'");
        await ExecuteAsync($"batch add '{{\"op\":\"patch\",\"id\":\"s1\",\"operations\":[{{\"op\":\"set\",\"path\":\"/status\",\"value\":\"done\"}}]}}'");

        var statusOutput = await ExecuteWithOutputAsync("batch status");
        var status = JsonDocument.Parse(statusOutput).RootElement;
        Assert.True(status.GetProperty("active").GetBoolean());
        Assert.Equal(2, status.GetProperty("operationCount").GetInt32());

        var execOutput = await ExecuteWithOutputAsync("batch execute");
        Assert.True(JsonDocument.Parse(execOutput).RootElement.GetProperty("success").GetBoolean());

        var item = JsonDocument.Parse(await ExecuteWithOutputAsync($"print s1 {pk}")).RootElement;
        Assert.Equal("done", item.GetProperty("status").GetString());

        var afterStatus = JsonDocument.Parse(await ExecuteWithOutputAsync("batch status")).RootElement;
        Assert.False(afterStatus.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task StatefulBatch_Cancel_DiscardsQueuedOperations()
    {
        await CreateBatchContainerAsync();
        const string pk = "tenant-cancel";

        await ExecuteAsync($"batch begin --partition-key {pk}");
        await ExecuteAsync($"batch add '{{\"op\":\"create\",\"item\":{{\"id\":\"x1\",\"pk\":\"{pk}\"}}}}'");
        await ExecuteAsync("batch cancel");

        var status = JsonDocument.Parse(await ExecuteWithOutputAsync("batch status")).RootElement;
        Assert.False(status.GetProperty("active").GetBoolean());

        var printState = await ExecuteAsync($"print x1 {pk}");
        Assert.True(printState.IsError);
    }

    [Fact]
    public async Task BatchAdd_WithoutBegin_ReturnsError()
    {
        await CreateBatchContainerAsync();

        var state = await ExecuteAsync("batch add '{\"op\":\"create\",\"item\":{\"id\":\"z\",\"pk\":\"p\"}}'");

        Assert.True(state.IsError);
    }

    [Fact]
    public async Task BatchExecute_WithoutBegin_ReturnsError()
    {
        await CreateBatchContainerAsync();

        var state = await ExecuteAsync("batch execute");

        Assert.True(state.IsError);
    }

    private async Task<string> CreateBatchContainerAsync()
    {
        var name = $"batch_{Guid.NewGuid():N}";
        var state = await ExecuteAsync($"mkcon {name} /pk");
        Assert.False(state.IsError, FormatError(state));
        await ExecuteAsync($"cd {name}");
        return name;
    }
}
