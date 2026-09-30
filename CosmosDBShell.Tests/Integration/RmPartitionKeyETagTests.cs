// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;

using Xunit;

public class RmPartitionKeyETagTests : EmulatorFixtureTestBase
{
    private string? containerName;

    public RmPartitionKeyETagTests(EmulatorDatabaseFixture fixture)
        : base(fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        await ExecuteAsync("cd");
        await ExecuteAsync($"cd {Fixture.DatabaseName}");
    }

    public override async ValueTask DisposeAsync()
    {
        if (this.containerName != null)
        {
            await ExecuteAsync("cd");
            await ExecuteAsync($"cd {Fixture.DatabaseName}");
            await ExecuteAsync($"rmcon {this.containerName} true");
        }

        await base.DisposeAsync();
    }

    [Fact]
    public async Task Rm_ExactIdWithPartitionKey_DryRunExposesTargetAndDeletesOnlyThatPartition()
    {
        await this.CreateContainerAsync("/pk");
        await this.CreateItemAsync(new { id = "dup", pk = "a", name = "first" });
        await this.CreateItemAsync(new { id = "dup", pk = "b", name = "second" });

        var preview = await this.RunJsonAsync("rm dup --key=id --partition-key=a --dry-run");
        Assert.Equal(1, preview.GetProperty("count").GetInt32());
        Assert.True(preview.GetProperty("dryRun").GetBoolean());
        Assert.Equal("dup", preview.GetProperty("id").GetString());
        Assert.Equal("a", preview.GetProperty("partitionKey").GetString());
        var etag = preview.GetProperty("etag").GetString();
        Assert.False(string.IsNullOrEmpty(etag));
        Assert.Equal(["a", "b"], await this.GetPartitionKeysAsync("dup"));

        var deleted = await this.RunJsonAsync($"rm dup --key=id --pk=a --etag '{etag}'");
        Assert.Equal(1, deleted.GetProperty("count").GetInt32());
        Assert.False(deleted.GetProperty("dryRun").GetBoolean());
        Assert.Equal(etag, deleted.GetProperty("ifMatchEtag").GetString());

        Assert.Equal(["b"], await this.GetPartitionKeysAsync("dup"));
    }

    [Fact]
    public async Task Rm_StaleETagAfterUpdate_RejectsDeletionWithoutRetry()
    {
        await this.CreateContainerAsync("/pk");
        await this.CreateItemAsync(new { id = "order-1", pk = "customer-42", status = "open" });
        var staleETag = (await this.RunJsonAsync("rm order-1 --key=id --pk=customer-42 --dry-run")).GetProperty("etag").GetString();

        var replaced = JsonSerializer.Serialize(new { id = "order-1", pk = "customer-42", status = "changed" });
        var replaceState = await ExecuteAsync($"replace '{replaced}'");
        Assert.False(replaceState.IsError, FormatError(replaceState));

        var dryRunState = await ExecuteAsync($"rm order-1 --key=id --pk=customer-42 --etag '{staleETag}' --dry-run");
        Assert.True(dryRunState.IsError);
        Assert.Contains("ETag mismatch", IntegrationTestBase.GetErrorMessage(dryRunState));

        var deleteState = await ExecuteAsync($"rm order-1 --key=id --pk=customer-42 --etag '{staleETag}'");
        Assert.True(deleteState.IsError);
        Assert.Contains("ETag mismatch", IntegrationTestBase.GetErrorMessage(deleteState));
        Assert.Equal(["customer-42"], await this.GetPartitionKeysAsync("order-1"));
    }

    [Fact]
    public async Task Rm_StaleETagAfterRecreation_RejectsDeletion()
    {
        await this.CreateContainerAsync("/pk");
        await this.CreateItemAsync(new { id = "order-2", pk = "customer-42" });
        var staleETag = (await this.RunJsonAsync("rm order-2 --key=id --pk=customer-42 --dry-run")).GetProperty("etag").GetString();

        await this.RunJsonAsync("rm order-2 --key=id --pk=customer-42");
        await this.CreateItemAsync(new { id = "order-2", pk = "customer-42" });

        var deleteState = await ExecuteAsync($"rm order-2 --key=id --pk=customer-42 --etag '{staleETag}'");
        Assert.True(deleteState.IsError);
        Assert.Contains("ETag mismatch", IntegrationTestBase.GetErrorMessage(deleteState));
        Assert.Equal(["customer-42"], await this.GetPartitionKeysAsync("order-2"));
    }

    [Fact]
    public async Task Rm_WildcardWithPartitionKey_PreviewAndExecutionHonorPartition()
    {
        await this.CreateContainerAsync("/pk");
        await this.CreateItemAsync(new { id = "tmp-1", pk = "a" });
        await this.CreateItemAsync(new { id = "tmp-2", pk = "a" });
        await this.CreateItemAsync(new { id = "tmp-3", pk = "b" });

        var preview = await this.RunJsonAsync("rm tmp-* --key=id --pk=a --dry-run");
        Assert.Equal(2, preview.GetProperty("count").GetInt32());
        Assert.Equal("a", preview.GetProperty("partitionKey").GetString());
        Assert.Equal(3, (await this.QueryIdsAsync("tmp-")).Count);

        var deleted = await this.RunJsonAsync("rm tmp-* --key=id --pk=a");
        Assert.Equal(2, deleted.GetProperty("count").GetInt32());
        Assert.Equal(["tmp-3"], await this.QueryIdsAsync("tmp-"));
    }

    [Fact]
    public async Task Rm_ExactIdWithTypedPartitionKey_TargetsMatchingType()
    {
        await this.CreateContainerAsync("/pk");
        await this.CreateItemAsync(new { id = "typed", pk = 7 });
        await this.CreateItemAsync(new { id = "typed", pk = "7" });

        var deleted = await this.RunJsonAsync("rm typed --key=id --pk=7");
        Assert.Equal(1, deleted.GetProperty("count").GetInt32());

        var remaining = await this.QueryAsync("typed");
        var item = Assert.Single(remaining);
        Assert.Equal(JsonValueKind.String, item.GetProperty("pk").ValueKind);
    }

    [Fact]
    public async Task Rm_ExactIdWithHierarchicalPartitionKey_RequiresCompleteKey()
    {
        await this.CreateContainerAsync("/tenant,/pk");
        await this.CreateItemAsync(new { id = "hpk", tenant = "tenant-1", pk = 7 });
        await this.CreateItemAsync(new { id = "hpk", tenant = "tenant-1", pk = 8 });

        var incomplete = await ExecuteAsync("rm hpk --key=id --pk '[\"tenant-1\"]' --dry-run");
        Assert.True(incomplete.IsError);
        Assert.Contains("all 2 components", IntegrationTestBase.GetErrorMessage(incomplete));

        var preview = await this.RunJsonAsync("rm hpk --key=id --pk '[\"tenant-1\",7]' --dry-run");
        Assert.Equal(1, preview.GetProperty("count").GetInt32());
        var partitionKey = preview.GetProperty("partitionKey");
        Assert.Equal("tenant-1", partitionKey[0].GetString());
        Assert.Equal(7, partitionKey[1].GetInt32());
        var etag = preview.GetProperty("etag").GetString();

        var deleted = await this.RunJsonAsync($"rm hpk --key=id --pk '[\"tenant-1\",7]' --etag '{etag}'");
        Assert.Equal(1, deleted.GetProperty("count").GetInt32());

        var remaining = Assert.Single(await this.QueryAsync("hpk"));
        Assert.Equal(8, remaining.GetProperty("pk").GetInt32());
    }

    private async Task CreateContainerAsync(string partitionKeyPaths)
    {
        this.containerName = $"rmpk{Guid.NewGuid():N}";
        var state = await ExecuteAsync($"mkcon {this.containerName} {partitionKeyPaths}");
        Assert.False(state.IsError, FormatError(state));
        var navigate = await ExecuteAsync($"cd {this.containerName}");
        Assert.False(navigate.IsError, FormatError(navigate));
    }

    private async Task CreateItemAsync(object item)
    {
        var state = await ExecuteAsync($"mkitem '{JsonSerializer.Serialize(item)}'");
        Assert.False(state.IsError, FormatError(state));
    }

    private async Task<JsonElement> RunJsonAsync(string command)
    {
        return JsonDocument.Parse(await ExecuteWithOutputAsync(command)).RootElement.Clone();
    }

    private async Task<List<JsonElement>> QueryAsync(string id)
    {
        var result = await this.RunJsonAsync($"query \"SELECT * FROM c WHERE c.id = '{id}'\"");
        return result.GetProperty("values").EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private async Task<List<string>> GetPartitionKeysAsync(string id)
    {
        return (await this.QueryAsync(id))
            .Select(item => item.GetProperty("pk").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<List<string>> QueryIdsAsync(string prefix)
    {
        var result = await this.RunJsonAsync($"query \"SELECT * FROM c WHERE STARTSWITH(c.id, '{prefix}')\"");
        return result.GetProperty("values").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
