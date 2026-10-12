// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Azure.ResourceManager;
using Azure.ResourceManager.CosmosDB;
using Microsoft.Azure.Cosmos;
using NSubstitute;

public class PrintCommandTests
{
    [Theory]
    [InlineData("680", "[680]")]
    [InlineData("680.5", "[680.5]")]
    [InlineData("\"680\"", "[\"680\"]")]
    [InlineData("true", "[true]")]
    [InlineData("false", "[false]")]
    [InlineData("null", "[null]")]
    [InlineData("\"true\"", "[\"true\"]")]
    [InlineData("\"null\"", "[\"null\"]")]
    [InlineData("customer-1", "[\"customer-1\"]")]
    [InlineData("001", "[\"001\"]")]
    [InlineData("-svc", "[\"-svc\"]")]
    [InlineData("", "[\"\"]")]
    [InlineData(" ", "[\" \"]")]
    [InlineData("[1001,\"customer-1001\"]", "[1001,\"customer-1001\"]")]
    [InlineData("[\"tenant-1\",680,true]", "[\"tenant-1\",680,true]")]
    [InlineData("[\"tenant-1\",null,false]", "[\"tenant-1\",null,false]")]
    public void TypedKey_PreservesTypesAndOrder(string value, string expected)
    {
        var command = new PrintCommand { PartitionKey = value };
        using var expectedJson = JsonDocument.Parse(expected);
        using var actualJson = JsonDocument.Parse(command.GetPartitionKey().ToString());

        Assert.True(JsonElement.DeepEquals(expectedJson.RootElement, actualJson.RootElement));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{},1]")]
    [InlineData("[[1],2]")]
    [InlineData("[1,]")]
    [InlineData("\"unterminated")]
    [InlineData("[]")]
    [InlineData("[1,2,3,4]")]
    [InlineData("1e400")]
    [InlineData("[\"tenant\",1e400]")]
    public void TypedKey_InvalidJsonOrComponents_ThrowsInputError(string value)
    {
        var command = new PrintCommand { PartitionKey = value };

        var exception = Assert.Throws<CommandException>(() => command.GetPartitionKey());

        Assert.Equal(MessageService.GetString("command-print-error-invalid_pk_json"), exception.Message);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Fact]
    public void MissingKey_ThrowsInputError()
    {
        var exception = Assert.Throws<CommandException>(() => new PrintCommand().GetPartitionKey());

        Assert.Equal(MessageService.GetString("command-print-error-missing_key"), exception.Message);
    }

    [Theory]
    [InlineData("680", 2)]
    [InlineData("[1001]", 2)]
    [InlineData("[1001,\"customer-1001\",true]", 2)]
    [InlineData("[\"tenant\",680]", 3)]
    public void IncompleteOrExtraComponents_ThrowsInputError(string value, int expected)
    {
        var key = new PrintCommand { PartitionKey = value }.GetPartitionKey();

        var exception = Assert.Throws<CommandException>(() => PrintCommand.ValidatePartitionKeyComponentCount(key, expected));

        Assert.Contains($"all {expected} components", exception.Message);
    }

    [Theory]
    [InlineData("680", 1)]
    [InlineData("null", 1)]
    [InlineData("[1001,\"customer-1001\"]", 2)]
    [InlineData("[\"tenant\",680,true]", 3)]
    public void CompleteKey_AcceptsMatchingComponentCount(string value, int expected)
    {
        var key = new PrintCommand { PartitionKey = value }.GetPartitionKey();

        PrintCommand.ValidatePartitionKeyComponentCount(key, expected);
    }

    [Theory]
    [InlineData("account", false)]
    [InlineData("database", false)]
    [InlineData("container", false)]
    [InlineData("account", true)]
    [InlineData("database", true)]
    [InlineData("container", true)]
    public async Task ExecuteAsync_ReadsMetadataOnceWithoutExistenceRequests(string location, bool typed)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var client = Substitute.For<CosmosClient>();
        var database = Substitute.For<Database>();
        var container = Substitute.For<Container>();
        client.GetDatabase("db").Returns(database);
        database.GetContainer("items").Returns(container);
        var metadata = Substitute.For<ContainerResponse>();
        metadata.Resource.Returns(new ContainerProperties("items", "/pk"));
        metadata.RequestCharge.Returns(1.25);
        container.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>()).Returns(metadata);
        using var response = new ResponseMessage(HttpStatusCode.OK)
        {
            Content = new MemoryStream(Encoding.UTF8.GetBytes("""{"id":"item-1","pk":"legacy"}""")),
        };
        response.Headers.Add("x-ms-request-charge", "2.5");
        container.ReadItemStreamAsync("item-1", new PartitionKey("legacy"), cancellationToken: Arg.Any<CancellationToken>()).Returns(response);
        shell.State = location switch
        {
            "container" => new ContainerState("items", "db", client),
            "database" => new DatabaseState("db", client),
            _ => new ConnectedState(client),
        };
        var command = new PrintCommand
        {
            Id = "item-1",
            Database = location == "account" ? "db" : null,
            Container = location == "container" ? null : "items",
            PartitionKey = typed ? "\"legacy\"" : "legacy",
        };
        using var scope = RequestChargeContext.Begin();

        var result = await command.ExecuteAsync(shell, new CommandState(), "print", TestContext.Current.CancellationToken);

        Assert.Equal("item-1", Assert.IsType<ShellJson>(result.Result).Value.GetProperty("id").GetString());
        Assert.Equal(3.75, scope.RequestCharge);
        Assert.DoesNotContain(database.ReceivedCalls(), call => call.GetMethodInfo().Name.StartsWith("Read", StringComparison.Ordinal));
        Assert.Single(container.ReceivedCalls(), call => call.GetMethodInfo().Name.StartsWith("ReadContainer", StringComparison.Ordinal));
        await container.Received(1).ReadItemStreamAsync("item-1", new PartitionKey("legacy"), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    public async Task ExecuteAsync_MetadataFailureReportsErrorAndNeverReadsItem(int status)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var client = Substitute.For<CosmosClient>();
        var database = Substitute.For<Database>();
        var container = Substitute.For<Container>();
        client.GetDatabase("db").Returns(database);
        database.GetContainer("items").Returns(container);
        var failure = new CosmosException("metadata unavailable", (HttpStatusCode)status, 0, "activity", 1.75);
        container.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task<ContainerResponse>>(_ => throw failure);
        shell.State = new ConnectedState(client);
        using var scope = RequestChargeContext.Begin();
        var command = new PrintCommand { Id = "item-1", PartitionKey = "680", Database = "db", Container = "items" };

        var error = await Assert.ThrowsAsync<CommandException>(() => command.ExecuteAsync(shell, new CommandState(), "print", TestContext.Current.CancellationToken));

        Assert.StartsWith("Error reading container metadata:", error.Message);
        Assert.Same(failure, error.InnerException);
        Assert.Equal(1.75, scope.RequestCharge);
        Assert.DoesNotContain(container.ReceivedCalls(), call => call.GetMethodInfo().Name == "ReadItemStreamAsync");
    }

    [Theory]
    [InlineData("database")]
    [InlineData("container")]
    [InlineData("forbidden")]
    public async Task ExecuteAsync_ArmMetadataFailureReportsErrorAndNeverReadsItem(string failure)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var client = Substitute.For<CosmosClient>();
        var container = Substitute.For<Container>();
        client.GetDatabase("db").GetContainer("items").Returns(container);
        var account = Substitute.For<CosmosDBAccountResource>();
        var databases = Substitute.For<CosmosDBSqlDatabaseCollection>();
        account.GetCosmosDBSqlDatabases().Returns(databases);
        var armDatabase = Substitute.For<CosmosDBSqlDatabaseResource>();
        var containers = Substitute.For<CosmosDBSqlContainerCollection>();
        armDatabase.GetCosmosDBSqlContainers().Returns(containers);
        databases.GetIfExistsAsync("db", Arg.Any<CancellationToken>()).Returns(Task.FromResult(failure == "database"
            ? Substitute.For<NullableResponse<CosmosDBSqlDatabaseResource>>()
            : Response.FromValue(armDatabase, Substitute.For<Response>())));
        if (failure == "forbidden")
        {
            containers.GetIfExistsAsync("items", Arg.Any<CancellationToken>())
                .Returns<Task<NullableResponse<CosmosDBSqlContainerResource>>>(_ => throw new RequestFailedException(403, "forbidden"));
        }
        else
        {
            containers.GetIfExistsAsync("items", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(Substitute.For<NullableResponse<CosmosDBSqlContainerResource>>()));
        }

        var id = new ResourceIdentifier("/subscriptions/s/resourceGroups/rg/providers/Microsoft.DocumentDB/databaseAccounts/a");
        var context = new ArmCosmosContext(Substitute.For<ArmClient>(), id, "s", "rg", "a", new Uri("https://a.documents.azure.com"), account);
        shell.State = new ConnectedState(client, context);
        var command = new PrintCommand { Id = "item-1", PartitionKey = "680", Database = "db", Container = "items" };

        var error = await Assert.ThrowsAsync<CommandException>(() => command.ExecuteAsync(shell, new CommandState(), "print", TestContext.Current.CancellationToken));

        Assert.StartsWith("Error reading container metadata:", error.Message);
        Assert.Equal(failure == "forbidden" ? 403 : 404, Assert.IsType<RequestFailedException>(error.InnerException).Status);
        Assert.DoesNotContain(container.ReceivedCalls(), call => call.GetMethodInfo().Name == "ReadItemStreamAsync");
    }
}
