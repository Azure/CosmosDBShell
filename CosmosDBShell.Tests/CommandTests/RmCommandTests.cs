// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

public class RmCommandTests
{
    private static CosmosClient CreateTestClient()
    {
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString("https://localhost:8081/");
        return new CosmosClient(connectionString);
    }

    [Fact]
    public async Task ExecuteAsync_NoPatternAndNoPipeInput_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new RmCommand { Pattern = null };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm", TestContext.Current.CancellationToken));
        Assert.Equal(MessageService.GetString("command-rm-error-no_filter"), ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new RmCommand { Pattern = "test-*" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm test-*", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_ConnectedWithoutDatabaseAndContainer_ThrowsNotInContainer()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new ConnectedState(CreateTestClient());
        var command = new RmCommand { Pattern = "test-*" };

        await Assert.ThrowsAsync<NotInContainerException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm test-*", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_InDatabaseWithoutContainer_ThrowsNotInContainer()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DatabaseState("TestDb", CreateTestClient());
        var command = new RmCommand { Pattern = "test-*" };

        await Assert.ThrowsAsync<NotInContainerException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm test-*", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_NoPattern_ThrowsCommandException()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var command = new RmCommand { Pattern = null, DryRun = true };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm --dry-run", TestContext.Current.CancellationToken));
        Assert.Equal(MessageService.GetString("command-rm-error-no_filter"), ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_Disconnected_ThrowsNotConnected()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new RmCommand { Pattern = "test-*", DryRun = true };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm test-* --dry-run", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void RmDryRunLocalizationKey_IsPresent()
    {
        var message = MessageService.GetString("command-rm-dry-run-plan", new Dictionary<string, object> { { "count", 3 } });

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain("command-rm-dry-run-plan", message);
    }

    [Fact]
    public void TryGetPartitionKeyElements_ReturnsAllHierarchicalValues()
    {
        using var document = JsonDocument.Parse("""
        {
          "id": "1",
          "tenantId": "tenant-a",
          "userId": "user-b",
          "sessionId": "session-c"
        }
        """);

        var found = RmCommand.TryGetPartitionKeyElements(
            document.RootElement,
            ["tenantId", "userId", "sessionId"],
            out var partitionKeyElements);

        Assert.True(found);
        Assert.Equal(["tenant-a", "user-b", "session-c"], partitionKeyElements.Select(element => element.GetString()!).ToArray());
    }

    [Fact]
    public void TryGetPartitionKeyElements_SupportsNestedHierarchicalValues()
    {
        using var document = JsonDocument.Parse("""
        {
          "id": "1",
          "tenant": {
            "id": "tenant-a"
          },
          "user": {
            "id": "user-b"
          }
        }
        """);

        var found = RmCommand.TryGetPartitionKeyElements(
            document.RootElement,
            ["tenant/id", "user/id"],
            out var partitionKeyElements);

        Assert.True(found);
        Assert.Equal(["tenant-a", "user-b"], partitionKeyElements.Select(element => element.GetString()!).ToArray());
    }

    [Fact]
    public void TryGetPartitionKeyElements_ReturnsFalseWhenAnyPathIsMissing()
    {
        using var document = JsonDocument.Parse("""
        {
          "id": "1",
          "tenantId": "tenant-a"
        }
        """);

        var found = RmCommand.TryGetPartitionKeyElements(
            document.RootElement,
            ["tenantId", "userId"],
            out var partitionKeyElements);

        Assert.False(found);
        Assert.Empty(partitionKeyElements);
    }

    [Theory]
    [InlineData("order-1", "customerId", "customer-42", "command-rm-error-etag_requires_exact_id")]
    [InlineData("order-*", "id", "customer-42", "command-rm-error-etag_requires_exact_id")]
    [InlineData("order-?", "id", "customer-42", "command-rm-error-etag_requires_exact_id")]
    [InlineData("order-1", null, "customer-42", "command-rm-error-etag_requires_exact_id")]
    [InlineData("order-1", "id", null, "command-rm-error-etag_requires_partition_key")]
    public async Task ExecuteAsync_InvalidETagCombination_FailsBeforeConnecting(string pattern, string? key, string? partitionKey, string expectedKey)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new RmCommand
        {
            Pattern = pattern,
            Key = key,
            PartitionKeyArgument = partitionKey,
            ETag = "\"etag\"",
        };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm", TestContext.Current.CancellationToken));
        Assert.Equal(MessageService.GetString(expectedKey), ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_EmptyETag_FailsBeforeConnecting(string etag)
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new RmCommand { Pattern = "order-1", Key = "id", PartitionKeyArgument = "customer-42", ETag = etag };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm", TestContext.Current.CancellationToken));
        Assert.Equal(MessageService.GetString("command-rm-error-etag_empty"), ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ETagWithPipeInput_FailsBeforeConnecting()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new RmCommand { Pattern = "order-1", Key = "id", PartitionKeyArgument = "customer-42", ETag = "\"etag\"" };
        var piped = new CommandState
        {
            Result = new ShellJson(JsonSerializer.SerializeToElement(new { id = "order-1", customerId = "customer-42" })),
        };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, piped, "rm", TestContext.Current.CancellationToken));
        Assert.Equal(MessageService.GetString("command-rm-error-etag_requires_exact_id"), ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ObjectPartitionKey_FailsBeforeConnecting()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new RmCommand { Pattern = "order-1", Key = "id", PartitionKeyArgument = "{\"a\":1}" };

        var ex = await Assert.ThrowsAsync<CommandException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm", TestContext.Current.CancellationToken));
        Assert.Equal(MessageService.GetString("command-rm-error-invalid_pk_json"), ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_PartitionKeyWithoutETag_KeepsExistingConnectionValidation()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.State = new DisconnectedState();
        var command = new RmCommand { Pattern = "order-1", Key = "id", PartitionKeyArgument = "customer-42" };

        await Assert.ThrowsAsync<NotConnectedException>(
            () => command.ExecuteAsync(shell, new CommandState(), "rm", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("customer-42", 1, "\"customer-42\"")]
    [InlineData("7", 1, "7")]
    [InlineData("true", 1, "true")]
    [InlineData("[\"customer-42\"]", 1, "\"customer-42\"")]
    [InlineData("[\"tenant-1\",7]", 2, "[\"tenant-1\",7]")]
    [InlineData("[\"tenant-1\",\"user-2\",\"session-3\"]", 3, "[\"tenant-1\",\"user-2\",\"session-3\"]")]
    public void ParsePartitionKey_ReportsComponentsAndTypedJson(string rawValue, int expectedComponents, string expectedJson)
    {
        var partitionKey = RmCommand.ParsePartitionKey(rawValue);

        Assert.Equal(expectedComponents, RmCommand.GetPartitionKeyComponentCount(partitionKey));
        Assert.Equal(expectedJson, RmCommand.ToPartitionKeyJson(partitionKey).GetRawText());
    }

    [Fact]
    public void ToPartitionKeyJson_PreservesFractionalNumbers()
    {
        var partitionKey = RmCommand.ParsePartitionKey("[\"tenant-1\",7.5]");

        Assert.Equal("[\"tenant-1\",7.5]", RmCommand.ToPartitionKeyJson(partitionKey).GetRawText());
    }

    [Theory]
    [InlineData("customer-42", """{"pk":"customer-42"}""", true)]
    [InlineData("customer-42", """{"pk":"customer-43"}""", false)]
    [InlineData("7", """{"pk":7}""", true)]
    [InlineData("7", """{"pk":"7"}""", false)]
    [InlineData("[\"tenant-1\",7]", """{"tenant":"tenant-1","pk":7}""", true)]
    [InlineData("[\"tenant-1\",7]", """{"tenant":"tenant-1","pk":8}""", false)]
    public void ParsePartitionKey_MatchesItemPartitionKeyByTypeAndValue(string rawValue, string itemJson, bool expected)
    {
        using var item = JsonDocument.Parse(itemJson);
        var paths = item.RootElement.TryGetProperty("tenant", out _) ? new[] { "tenant", "pk" } : ["pk"];
        Assert.True(RmCommand.TryGetPartitionKeyElements(item.RootElement, paths, out var elements));

        var scope = RmCommand.ParsePartitionKey(rawValue);

        Assert.Equal(expected, scope.Equals(RmCommand.CreateItemPartitionKey(elements)));
    }

    [Fact]
    public void RmNewLocalizationKeys_ArePresent()
    {
        string[] keys =
        [
            "command-rm-description-partition-key",
            "command-rm-description-etag",
            "command-rm-error-invalid_pk_json",
            "command-rm-error-etag_empty",
            "command-rm-error-etag_requires_exact_id",
            "command-rm-error-etag_requires_partition_key",
            "command-rm-example-6",
            "command-rm-example-7",
        ];

        foreach (var key in keys)
        {
            var message = MessageService.GetString(key);
            Assert.False(string.IsNullOrWhiteSpace(message));
            Assert.DoesNotContain(key, message);
        }

        var mismatch = MessageService.GetString("command-rm-error-etag_mismatch", new Dictionary<string, object> { { "id", "order-1" } });
        Assert.Contains("order-1", mismatch);
        var components = MessageService.GetString(
            "command-rm-error-partition_key_components",
            new Dictionary<string, object> { { "expected", 2 }, { "actual", 1 } });
        Assert.Contains("2", components);
        Assert.Contains("1 was", components);
    }
}
