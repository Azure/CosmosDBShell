// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Net;
using Azure;
using Azure.Core;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Azure.ResourceManager;
using Azure.ResourceManager.CosmosDB;
using Azure.ResourceManager.CosmosDB.Models;
using Microsoft.Azure.Cosmos;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

public class ServerlessCreationThroughputTests
{
    private const string ServerlessMessage = "Setting offer throughput or autopilot on container is not supported for serverless accounts.";

    [Fact]
    public void CreateUpdateConfig_ServerlessWithoutOptions_OmitsThroughput()
    {
        Assert.Null(CosmosArmResourceProvider.CreateUpdateConfig(ServerlessByCapability(), null, null));
        Assert.Null(CosmosArmResourceProvider.CreateUpdateConfig(ServerlessByCapacityMode(), null, null));
    }

    [Theory]
    [InlineData("manual", null)]
    [InlineData("auto", null)]
    [InlineData(null, 400)]
    [InlineData("m", 1000)]
    public void CreateUpdateConfig_ServerlessWithOptions_Throws(string? scale, int? ru)
    {
        Assert.Throws<ServerlessThroughputNotSupportedException>(
            () => CosmosArmResourceProvider.CreateUpdateConfig(ServerlessByCapability(), scale, ru));
    }

    [Theory]
    [InlineData(null, null, null, 1000)]
    [InlineData("auto", 4000, null, 4000)]
    [InlineData("manual", 400, 400, null)]
    [InlineData("m", null, 1000, null)]
    public void CreateUpdateConfig_Provisioned_KeepsExistingDefaults(string? scale, int? ru, int? manual, int? autoscale)
    {
        var config = CosmosArmResourceProvider.CreateUpdateConfig(Provisioned(), scale, ru);

        Assert.NotNull(config);
        Assert.Equal(manual, config!.Throughput);
        Assert.Equal(autoscale, config.AutoscaleMaxThroughput);
    }

    [Fact]
    public async Task ArmCreateDatabase_Serverless_SendsPayloadWithoutThroughput()
    {
        var (context, databases) = CreateArmContext(ServerlessByCapability());
        CosmosDBSqlDatabaseCreateOrUpdateContent? sent = null;
        databases.CreateOrUpdateAsync(WaitUntil.Completed, "db", Arg.Do<CosmosDBSqlDatabaseCreateOrUpdateContent>(c => sent = c), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<ArmOperation<CosmosDBSqlDatabaseResource>>());

        await CosmosArmResourceProvider.CreateDatabaseAsync(context, "db", null, null, TestContext.Current.CancellationToken);

        var json = CosmosArmResourceProvider.WriteArmModel(Assert.IsType<CosmosDBSqlDatabaseCreateOrUpdateContent>(sent));
        Assert.DoesNotContain("throughput", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("autoscaleSettings", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArmCreateDatabase_ServerlessWithOptions_ThrowsBeforeSending()
    {
        var (context, databases) = CreateArmContext(ServerlessByCapacityMode());

        await Assert.ThrowsAsync<ServerlessThroughputNotSupportedException>(
            () => CosmosArmResourceProvider.CreateDatabaseAsync(context, "db", "manual", 400, TestContext.Current.CancellationToken));
        await databases.DidNotReceiveWithAnyArgs().CreateOrUpdateAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task ArmCreateDatabase_Provisioned_SendsDefaultAutoscale()
    {
        var (context, databases) = CreateArmContext(Provisioned());
        CosmosDBSqlDatabaseCreateOrUpdateContent? sent = null;
        databases.CreateOrUpdateAsync(WaitUntil.Completed, "db", Arg.Do<CosmosDBSqlDatabaseCreateOrUpdateContent>(c => sent = c), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<ArmOperation<CosmosDBSqlDatabaseResource>>());

        await CosmosArmResourceProvider.CreateDatabaseAsync(context, "db", null, null, TestContext.Current.CancellationToken);

        Assert.Equal(1000, sent!.Options.AutoscaleMaxThroughput);
        Assert.Contains("autoscaleSettings", CosmosArmResourceProvider.WriteArmModel(sent), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fallback_ServerlessRejectionWithoutOptions_RetriesWithoutThroughput()
    {
        var sent = new List<ThroughputProperties?>();

        var result = await DataPlaneCosmosResourceOperations.CreateWithServerlessFallbackAsync(null, null, throughput =>
        {
            sent.Add(throughput);
            return sent.Count == 1 ? throw ServerlessRejection() : Task.FromResult("created");
        });

        Assert.Equal("created", result);
        Assert.Equal(2, sent.Count);
        Assert.Equal(1000, sent[0]!.AutoscaleMaxThroughput);
        Assert.Null(sent[1]);
    }

    [Theory]
    [InlineData("manual", 400)]
    [InlineData("auto", null)]
    [InlineData(null, 1000)]
    public async Task Fallback_ServerlessRejectionWithOptions_ThrowsWithoutRetrying(string? scale, int? ru)
    {
        var calls = 0;

        var ex = await Assert.ThrowsAsync<ServerlessThroughputNotSupportedException>(() =>
            DataPlaneCosmosResourceOperations.CreateWithServerlessFallbackAsync<string>(scale, ru, _ =>
            {
                calls++;
                throw ServerlessRejection();
            }));

        Assert.Equal(1, calls);
        Assert.IsType<CosmosException>(ex.InnerException);
    }

    public static TheoryData<Exception> UnrelatedFailures => new()
    {
        new CosmosException("Request rate is large", HttpStatusCode.BadRequest, 0, "a", 0),
        new CosmosException("serverless principal is not authorized", HttpStatusCode.Forbidden, 0, "a", 0),
        new CosmosException("serverless token invalid", HttpStatusCode.Unauthorized, 0, "a", 0),
        new OperationCanceledException(),
    };

    [Theory]
    [MemberData(nameof(UnrelatedFailures))]
    public async Task Fallback_UnrelatedFailure_PropagatesWithoutRetrying(Exception failure)
    {
        var calls = 0;

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            DataPlaneCosmosResourceOperations.CreateWithServerlessFallbackAsync<string>(null, null, _ =>
            {
                calls++;
                throw failure;
            }));

        Assert.Same(failure, thrown);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Mkcon_ServerlessDataPlane_RetriesWithSameContainerDefinition()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var (client, database) = CreateDataPlaneClient();
        var sentThroughput = new List<ThroughputProperties?>();
        var sentProperties = new List<ContainerProperties>();
        var created = Substitute.For<ContainerResponse>();
        created.Container.Id.Returns("Items");
        database.CreateContainerIfNotExistsAsync(
                Arg.Do<ContainerProperties>(sentProperties.Add),
                Arg.Do<ThroughputProperties?>(sentThroughput.Add),
                Arg.Any<RequestOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<ContainerResponse>(ServerlessRejection()), _ => Task.FromResult(created));
        shell.State = new DatabaseState("db", client);

        var result = await new MakeContainerCommand
        {
            Name = "Items",
            PartitionKey = "/tenantId,/userId",
            UniqueKey = "/email",
            IndexPolicy = "{\"indexingMode\":\"consistent\"}",
        }.ExecuteAsync(shell, new CommandState(), "mkcon", TestContext.Current.CancellationToken);

        Assert.Equal("Items", ((ShellJson)result.Result!).Value.GetProperty("id").GetString());
        Assert.Equal(1000, sentThroughput[0]!.AutoscaleMaxThroughput);
        Assert.Null(sentThroughput[1]);
        Assert.Same(sentProperties[0], sentProperties[1]);
        Assert.Equal(["/tenantId", "/userId"], sentProperties[1].PartitionKeyPaths);
        Assert.Equal("/email", Assert.Single(Assert.Single(sentProperties[1].UniqueKeyPolicy.UniqueKeys).Paths));
        Assert.Equal(IndexingMode.Consistent, sentProperties[1].IndexingPolicy.IndexingMode);
    }

    [Theory]
    [InlineData("mkdb")]
    [InlineData("create")]
    public async Task CreateDatabase_ServerlessDataPlane_RetriesWithoutThroughput(string entryPoint)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var (client, _) = CreateDataPlaneClient();
        var sent = new List<ThroughputProperties?>();
        var created = Substitute.For<DatabaseResponse>();
        created.Database.Id.Returns("NewDb");
        client.CreateDatabaseIfNotExistsAsync("NewDb", Arg.Do<ThroughputProperties?>(sent.Add), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<DatabaseResponse>(ServerlessRejection()), _ => Task.FromResult(created));
        shell.State = new ConnectedState(client);

        CosmosCommand command = entryPoint == "mkdb"
            ? new MakeDbCommand { Name = "NewDb" }
            : new CreateCommand { Item = "db", Name = "NewDb" };
        await command.ExecuteAsync(shell, new CommandState(), entryPoint, TestContext.Current.CancellationToken);

        Assert.Equal(2, sent.Count);
        Assert.Null(sent[1]);
    }

    [Fact]
    public async Task Mkcon_ServerlessWithExplicitRu_ReportsActionableError()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var (client, database) = CreateDataPlaneClient();
        database.CreateContainerIfNotExistsAsync(Arg.Any<ContainerProperties>(), Arg.Any<ThroughputProperties?>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ServerlessRejection());
        shell.State = new DatabaseState("db", client);

        var ex = await Assert.ThrowsAsync<CommandException>(() => new CreateCommand { Item = "container", Name = "Items", PartitionKey = "/pk", MaxRU = 400 }
            .ExecuteAsync(shell, new CommandState(), "create container", TestContext.Current.CancellationToken));

        Assert.Equal(MessageService.GetString("error-serverless_throughput_not_supported"), ex.Message);
        await database.ReceivedWithAnyArgs(1).CreateContainerIfNotExistsAsync(default(ContainerProperties)!, default(ThroughputProperties?), default, default);
    }

    [Fact]
    public async Task Mkdb_ProvisionedSharedThroughput_SendsRequestedManualThroughputOnce()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var (client, _) = CreateDataPlaneClient();
        var sent = new List<ThroughputProperties?>();
        var created = Substitute.For<DatabaseResponse>();
        created.Database.Id.Returns("Shared");
        client.CreateDatabaseIfNotExistsAsync("Shared", Arg.Do<ThroughputProperties?>(sent.Add), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(created);
        shell.State = new ConnectedState(client);

        await new MakeDbCommand { Name = "Shared", Scale = "manual", MaxRU = 400 }
            .ExecuteAsync(shell, new CommandState(), "mkdb", TestContext.Current.CancellationToken);

        Assert.Equal(400, Assert.Single(sent)!.Throughput);
    }

    [Fact]
    public async Task Mkcon_ProvisionedAccount_KeepsDefaultAutoscale()
    {
        using var shell = ShellInterpreter.CreateInstance();
        var (client, database) = CreateDataPlaneClient();
        var sent = new List<ThroughputProperties?>();
        var created = Substitute.For<ContainerResponse>();
        created.Container.Id.Returns("Items");
        database.CreateContainerIfNotExistsAsync(Arg.Any<ContainerProperties>(), Arg.Do<ThroughputProperties?>(sent.Add), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(created);
        shell.State = new DatabaseState("db", client);

        await new MakeContainerCommand { Name = "Items", PartitionKey = "/pk" }
            .ExecuteAsync(shell, new CommandState(), "mkcon", TestContext.Current.CancellationToken);

        Assert.Equal(1000, Assert.Single(sent)!.AutoscaleMaxThroughput);
    }

    private static CosmosException ServerlessRejection() => new(ServerlessMessage, HttpStatusCode.BadRequest, 0, "a", 1.5);

    private static (CosmosClient Client, Database Database) CreateDataPlaneClient()
    {
        var client = Substitute.For<CosmosClient>();
        var database = Substitute.For<Database>();
        var existing = Substitute.For<DatabaseResponse>();
        existing.StatusCode.Returns(HttpStatusCode.OK);
        database.ReadAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>()).Returns(existing);
        client.GetDatabase(Arg.Any<string>()).Returns(database);
        return (client, database);
    }

    private static (ArmCosmosContext Context, CosmosDBSqlDatabaseCollection Databases) CreateArmContext(CosmosDBAccountData data)
    {
        var account = Substitute.For<CosmosDBAccountResource>();
        account.Data.Returns(data);
        var databases = Substitute.For<CosmosDBSqlDatabaseCollection>();
        account.GetCosmosDBSqlDatabases().Returns(databases);
        var id = new ResourceIdentifier("/subscriptions/s/resourceGroups/rg/providers/Microsoft.DocumentDB/databaseAccounts/a");
        return (new ArmCosmosContext(Substitute.For<ArmClient>(), id, "s", "rg", "a", new Uri("https://a.documents.azure.com"), account), databases);
    }

    private static CosmosDBAccountData ServerlessByCapability() => ArmCosmosDBModelFactory.CosmosDBAccountData(
        location: AzureLocation.WestUS,
        capabilities: [new CosmosDBAccountCapability { Name = "EnableServerless" }]);

    private static CosmosDBAccountData ServerlessByCapacityMode() => ArmCosmosDBModelFactory.CosmosDBAccountData(
        location: AzureLocation.WestUS,
        capacityMode: CapacityMode.Serverless);

    private static CosmosDBAccountData Provisioned() => ArmCosmosDBModelFactory.CosmosDBAccountData(
        location: AzureLocation.WestUS,
        capabilities: [new CosmosDBAccountCapability { Name = "EnableServerlessLike" }],
        capacityMode: CapacityMode.Provisioned);
}
