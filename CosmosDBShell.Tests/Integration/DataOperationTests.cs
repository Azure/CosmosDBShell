// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;

using Xunit;

public class DataOperationTests : EmulatorFixtureTestBase
{
    public DataOperationTests(EmulatorDatabaseFixture fixture)
        : base(fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        await ExecuteAsync("cd");
        await ExecuteAsync($"cd {Fixture.DatabaseName}/{Fixture.ContainerName}");
    }

    [Fact]
    public async Task MkItem_CreatesItem_LsShowsIt()
    {
        var id = $"item-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new { id, name = "test-item" });
        var mkOutput = await ExecuteWithOutputAsync($"mkitem '{json}'");
        var mkJson = JsonDocument.Parse(mkOutput).RootElement;
        Assert.Equal("success", mkJson.GetProperty("result").GetString());

        var lsOutput = await ExecuteWithOutputAsync("ls");
        var lsJson = JsonDocument.Parse(lsOutput).RootElement;
        var items = lsJson.GetProperty("items");
        Assert.Contains(items.EnumerateArray(), item =>
            item.GetProperty("id").GetString() == id &&
            item.GetProperty("name").GetString() == "test-item");
    }

    [Fact]
    public async Task MkItem_Force_ReplacesExistingItem()
    {
        var id = $"force-{Guid.NewGuid():N}";
        var original = JsonSerializer.Serialize(new { id, name = "before" });
        var updated = JsonSerializer.Serialize(new { id, name = "after" });

        await ExecuteAsync($"mkitem '{original}'");

        var forceOutput = await ExecuteWithOutputAsync($"mkitem --force '{updated}'");
        var forceJson = JsonDocument.Parse(forceOutput).RootElement;
        Assert.Equal("success", forceJson.GetProperty("result").GetString());

        var output = await ExecuteWithOutputAsync($"print {id} {id}");
        var item = JsonDocument.Parse(output).RootElement;
        Assert.Equal("after", item.GetProperty("name").GetString());
    }

    [Fact]
    public async Task CreateItem_ForceAndUpsert_ReplaceExistingItem()
    {
        var id = $"create-force-{Guid.NewGuid():N}";
        var original = JsonSerializer.Serialize(new { id, name = "before" });
        var forceUpdated = JsonSerializer.Serialize(new { id, name = "force" });
        var upsertUpdated = JsonSerializer.Serialize(new { id, name = "upsert" });

        await ExecuteAsync($"create item '{original}'");

        var forceOutput = await ExecuteWithOutputAsync($"create item '{forceUpdated}' --force");
        var forceJson = JsonDocument.Parse(forceOutput).RootElement;
        Assert.Equal("success", forceJson.GetProperty("result").GetString());

        var upsertOutput = await ExecuteWithOutputAsync($"create item '{upsertUpdated}' --upsert");
        var upsertJson = JsonDocument.Parse(upsertOutput).RootElement;
        Assert.Equal("success", upsertJson.GetProperty("result").GetString());

        var output = await ExecuteWithOutputAsync($"print {id} {id}");
        var item = JsonDocument.Parse(output).RootElement;
        Assert.Equal("upsert", item.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Replace_UpdatesExistingItem()
    {
        var id = $"replace-{Guid.NewGuid():N}";
        var original = JsonSerializer.Serialize(new { id, name = "before" });
        var updated = JsonSerializer.Serialize(new { id, name = "after" });

        await ExecuteAsync($"mkitem '{original}'");

        var replaceOutput = await ExecuteWithOutputAsync($"replace '{updated}'");
        var replaceJson = JsonDocument.Parse(replaceOutput).RootElement;
        Assert.Equal("success", replaceJson.GetProperty("result").GetString());

        var output = await ExecuteWithOutputAsync($"print {id} {id}");
        var item = JsonDocument.Parse(output).RootElement;
        Assert.Equal("after", item.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Replace_RejectsNonStringId()
    {
        var payload = JsonSerializer.Serialize(new { id = 123, name = "invalid" });

        var state = await ExecuteAsync($"replace '{payload}'");

        Assert.True(state.IsError);
        Assert.Equal("Each item must include a non-empty 'id' property.", IntegrationTestBase.GetErrorMessage(state));
    }

    [Fact]
    public async Task Replace_ArrayWithEtag_ReturnsError()
    {
        var id = $"replace-etag-array-{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new[] { new { id, name = "value" } });

        var state = await ExecuteAsync($"replace '{payload}' --etag=etag-value");

        Assert.True(state.IsError);
        Assert.Equal("The --etag option can only be used when replacing a single item.", IntegrationTestBase.GetErrorMessage(state));
    }

    [Fact]
    public async Task Replace_ArrayFailures_ReturnsErrorState()
    {
        var id = $"replace-array-failure-{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new[] { new { id, name = "missing" } });

        var state = await ExecuteAsync($"replace '{payload}'");

        Assert.True(state.IsError);
        Assert.Equal("Failed to replace 1 of 1 items.", IntegrationTestBase.GetErrorMessage(state));
    }

    [Fact]
    public async Task Patch_UpdatesField()
    {
        var id = $"patch-{Guid.NewGuid():N}";
        var original = JsonSerializer.Serialize(new { id, name = "before", count = 1 });

        await ExecuteAsync($"mkitem '{original}'");

        var setOutput = await ExecuteWithOutputAsync($"patch set {id} {id} /name after");
        var setJson = JsonDocument.Parse(setOutput).RootElement;
        Assert.Equal("success", setJson.GetProperty("result").GetString());

        var incrOutput = await ExecuteWithOutputAsync($"patch incr {id} {id} /count 2");
        var incrJson = JsonDocument.Parse(incrOutput).RootElement;
        Assert.Equal("success", incrJson.GetProperty("result").GetString());

        var output = await ExecuteWithOutputAsync($"print {id} {id}");
        var item = JsonDocument.Parse(output).RootElement;
        Assert.Equal("after", item.GetProperty("name").GetString());
        Assert.Equal(3, item.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Replace_FailsWhenItemDoesNotExist()
    {
        var id = $"replace-missing-{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new { id, name = "missing" });

        var state = await ExecuteAsync($"replace '{payload}'");
        Assert.True(state.IsError);
        var message = IntegrationTestBase.GetErrorMessage(state);
        Assert.Equal($"Item '{id}' not found.", message);
        Assert.DoesNotContain("ActivityId", message);
    }

    [Fact]
    public async Task Patch_FailsWhenItemDoesNotExist()
    {
        var id = $"patch-missing-{Guid.NewGuid():N}";

        var state = await ExecuteAsync($"patch set {id} {id} /name after");
        Assert.True(state.IsError);
        var message = IntegrationTestBase.GetErrorMessage(state);
        Assert.Equal($"Item '{id}' not found.", message);
        Assert.DoesNotContain("ActivityId", message);
    }

    [Fact]
    public async Task Patch_FailsWhenPathDoesNotStartWithSlash()
    {
        var state = await ExecuteAsync("patch set item item name after");
        Assert.True(state.IsError);
        Assert.Equal("Patch path must start with '/'.", IntegrationTestBase.GetErrorMessage(state));
    }

    [Fact]
    public async Task Patch_FailsWithUsageWhenOperationIsNotFirst()
    {
        var state = await ExecuteAsync("patch 0 1 set test2 \"Bar Baz\"");
        Assert.True(state.IsError);
        Assert.Equal(
            "Unsupported patch operation '0'. Usage: patch <op> <id> <pk> <path> [value]. Supported operations: set, add, replace, remove, incr.",
            IntegrationTestBase.GetErrorMessage(state));
    }

    [Fact]
    public async Task Patch_RemovesField()
    {
        var id = $"patch-remove-{Guid.NewGuid():N}";
        var original = JsonSerializer.Serialize(new { id, name = "before", note = "delete-me" });

        await ExecuteAsync($"mkitem '{original}'");
        await ExecuteAsync($"patch remove {id} {id} /note");

        var output = await ExecuteWithOutputAsync($"print {id} {id}");
        var item = JsonDocument.Parse(output).RootElement;
        Assert.False(item.TryGetProperty("note", out _));
    }

    [Fact]
    public async Task Patch_TypedValuesAreParsedAsJson()
    {
        var id = $"patch-typed-{Guid.NewGuid():N}";
        var original = JsonSerializer.Serialize(new { id, name = "typed" });

        await ExecuteAsync($"mkitem '{original}'");
        await ExecuteAsync($"patch set {id} {id} /enabled true");
        await ExecuteAsync($"patch set {id} {id} /score 7");

        var output = await ExecuteWithOutputAsync($"print {id} {id}");
        var item = JsonDocument.Parse(output).RootElement;
        Assert.True(item.GetProperty("enabled").GetBoolean());
        Assert.Equal(7, item.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task PatchAndReplace_HierarchicalPartitionKey_Succeed()
    {
        var containerName = $"hpk{Guid.NewGuid():N}";
        var id = $"hpk-item-{Guid.NewGuid():N}";

        await ExecuteAsync("cd");
        await ExecuteAsync($"cd {Fixture.DatabaseName}");

        try
        {
            var createState = await ExecuteAsync($"mkcon {containerName} /tenant,/pk");
            Assert.False(createState.IsError, FormatError(createState));

            var navigateState = await ExecuteAsync($"cd {containerName}");
            Assert.False(navigateState.IsError, FormatError(navigateState));

            var original = JsonSerializer.Serialize(new { id, tenant = "tenant-1", pk = 7, name = "before" });
            var createItemState = await ExecuteAsync($"mkitem '{original}'");
            Assert.False(createItemState.IsError, FormatError(createItemState));

            var patchState = await ExecuteAsync($"patch set {id} '[\"tenant-1\",7]' /name patched");
            Assert.False(patchState.IsError, FormatError(patchState));

            var replaced = JsonSerializer.Serialize(new { id, tenant = "tenant-1", pk = 7, name = "replaced" });
            var replaceState = await ExecuteAsync($"replace '{replaced}'");
            Assert.False(replaceState.IsError, FormatError(replaceState));

            var output = await ExecuteWithOutputAsync($"query \"SELECT * FROM c WHERE c.id = '{id}'\"");
            var queryJson = JsonDocument.Parse(output).RootElement;
            var items = queryJson.GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            Assert.Equal("replaced", items[0].GetProperty("name").GetString());
        }
        finally
        {
            await ExecuteAsync("cd");
            await ExecuteAsync($"cd {Fixture.DatabaseName}");
            await ExecuteAsync($"rmcon {containerName} true");
            await ExecuteAsync("cd");
            await ExecuteAsync($"cd {Fixture.DatabaseName}/{Fixture.ContainerName}");
        }
    }

    [Fact]
    public async Task Patch_RemoveRejectsValueArgument()
    {
        var state = await ExecuteAsync("patch remove item item /field extra");
        Assert.True(state.IsError);
        Assert.Equal("Patch operation 'remove' does not take a value.", IntegrationTestBase.GetErrorMessage(state));
    }

    [Fact]
    public async Task Patch_RemoveRejectsEmptyStringValueArgument()
    {
        var state = await ExecuteAsync("patch remove item item /field \"\"");
        Assert.True(state.IsError);
        Assert.Equal("Patch operation 'remove' does not take a value.", IntegrationTestBase.GetErrorMessage(state));
    }

    [Fact]
    public async Task Query_SelectAll_ReturnsItems()
    {
        var id = $"query-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new { id, name = "for-query" });
        await ExecuteAsync($"mkitem '{json}'");

        var output = await ExecuteWithOutputAsync($"query \"SELECT * FROM c WHERE c.id = '{id}'\"");
        var queryJson = JsonDocument.Parse(output).RootElement;
        var items = queryJson.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(id, items[0].GetProperty("id").GetString());
        Assert.Equal("for-query", items[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Print_ById_ReturnsItem()
    {
        var id = $"print-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new { id, name = "for-print" });
        await ExecuteAsync($"mkitem '{json}'");

        var output = await ExecuteWithOutputAsync($"print {id} {id}");
        var printedItem = JsonDocument.Parse(output).RootElement;
        Assert.Equal(id, printedItem.GetProperty("id").GetString());
        Assert.Equal("for-print", printedItem.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Rm_DeletesItem_NoLongerInLs()
    {
        var id = $"rm-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new { id, name = "for-rm" });
        await ExecuteAsync($"mkitem '{json}'");

        var rmState = await ExecuteAsync($"rm \"{id}\"");
        Assert.False(rmState.IsError, IntegrationTestBase.FormatError(rmState));

        var lsOutput = await ExecuteWithOutputAsync("ls");
        var lsJson = JsonDocument.Parse(lsOutput).RootElement;
        var items = lsJson.GetProperty("items");
        Assert.DoesNotContain(items.EnumerateArray(), item => item.GetProperty("id").GetString() == id);
    }
}