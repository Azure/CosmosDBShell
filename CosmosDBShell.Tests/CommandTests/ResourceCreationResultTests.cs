// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Net;
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
using Azure.ResourceManager.CosmosDB.Models;
using Microsoft.Azure.Cosmos;
using NSubstitute;
using Spectre.Console;

[Collection(ConsoleOutputTestCollection.Name)]
public class ResourceCreationResultTests
{
    public static IEnumerable<object?[]> CreationCases()
    {
        foreach (var container in new[] { false, true })
        {
            foreach (var longForm in new[] { false, true })
            {
                foreach (bool? created in new bool?[] { true, false, null })
                {
                    foreach (var format in new[] { "user", "json" })
                    {
                        yield return [container, longForm, created, format];
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(CreationCases))]
    public async Task Commands_ReportBackendOutcomeInStructuredAndUserOutput(bool container, bool longForm, bool? created, string format)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var client = Substitute.For<CosmosClient>();
        shell.Options = new Program.CosmosShellOptions { Output = format };
        if (created.HasValue)
        {
            ConfigureDataPlane(client, created.Value);
            shell.State = new ConnectedState(client);
        }
        else
        {
            shell.State = new ConnectedState(client, ConfigureArm());
        }

        CosmosCommand command = (container, longForm) switch
        {
            (false, false) => new MakeDbCommand { Name = "db" },
            (false, true) => new CreateCommand { Item = "database", Name = "db" },
            (true, false) => new MakeContainerCommand { Name = "Items", PartitionKey = "/pk", Database = "db" },
            (true, true) => new CreateCommand { Item = "container", Name = "Items", PartitionKey = "/pk", Database = "db" },
        };
        var state = await command.ExecuteAsync(shell, new CommandState(), string.Empty, TestContext.Current.CancellationToken);
        var result = Assert.IsType<ShellJson>(state.Result).Value;
        var outcome = created switch { true => "created", false => "existing", null => "createdOrUpdated" };
        Assert.Equal(container ? "container" : "database", result.GetProperty("type").GetString());
        Assert.Equal(container ? "Items" : "db", result.GetProperty("id").GetString());
        Assert.Equal(outcome, result.GetProperty("outcome").GetString());
        if (created.HasValue)
        {
            Assert.Equal(created.Value, result.GetProperty("created").GetBoolean());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, result.GetProperty("created").ValueKind);
            await client.DidNotReceiveWithAnyArgs().CreateDatabaseIfNotExistsAsync(default!, default(ThroughputProperties), default, TestContext.Current.CancellationToken);
        }

        var originalOut = Console.Out;
        var originalConsole = AnsiConsole.Console;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(output),
            });
            shell.PrintState(state);
            if (format == "json")
            {
                using var document = JsonDocument.Parse(output.ToString());
                Assert.True(JsonElement.DeepEquals(result, document.RootElement));
            }
            else
            {
                var key = (container, created) switch
                {
                    (false, true) => "command-mkdb-database_created",
                    (false, false) => "command-mkdb-database_existing",
                    (false, null) => "command-mkdb-database_created_or_updated",
                    (true, true) => "command-mkcon-CreatedContainer",
                    (true, false) => "command-mkcon-container_existing",
                    (true, null) => "command-mkcon-container_created_or_updated",
                };
                var expected = MessageService.GetArgsString(key, container ? "container" : "db", container ? "Items" : "db");
                Assert.Equal(expected + Environment.NewLine, output.ToString());
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            AnsiConsole.Console = originalConsole;
        }
    }

    [Theory]
    [InlineData(false, 200)]
    [InlineData(false, 202)]
    [InlineData(true, 200)]
    [InlineData(true, 202)]
    public async Task ArmCreation_DoesNotInferCreationFromResponseOrExistence(bool container, int status)
    {
        var context = ConfigureArm(status);
        var operations = new ArmCosmosResourceOperations(context);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = container
                ? await operations.CreateContainerAsync("db", "Items", ["/pk"], null, null, null, null, TestContext.Current.CancellationToken)
                : await operations.CreateDatabaseAsync("db", null, null, TestContext.Current.CancellationToken);
            Assert.Equal(container ? "Items" : "db", result.Id);
            Assert.Null(result.Created);
            Assert.Equal("createdOrUpdated", result.Outcome);
        }

        var databases = context.Account.GetCosmosDBSqlDatabases();
        if (container)
        {
            var database = await databases.GetIfExistsAsync("db", TestContext.Current.CancellationToken);
            await database.Value!.GetCosmosDBSqlContainers().DidNotReceiveWithAnyArgs()
                .GetIfExistsAsync(default!, TestContext.Current.CancellationToken);
        }
        else
        {
            await databases.DidNotReceiveWithAnyArgs().GetIfExistsAsync(default!, TestContext.Current.CancellationToken);
        }
    }

    private static void ConfigureDataPlane(CosmosClient client, bool created)
    {
        var database = Substitute.For<Database>();
        database.Id.Returns("db");
        client.GetDatabase("db").Returns(database);
        var existing = Substitute.For<DatabaseResponse>();
        existing.StatusCode.Returns(HttpStatusCode.OK);
        database.ReadAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>()).Returns(existing);
        var databaseResponse = Substitute.For<DatabaseResponse>();
        databaseResponse.Database.Returns(database);
        databaseResponse.StatusCode.Returns(created ? HttpStatusCode.Created : HttpStatusCode.OK);
        client.CreateDatabaseIfNotExistsAsync("db", Arg.Any<ThroughputProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(databaseResponse);
        var containerResponse = Substitute.For<ContainerResponse>();
        containerResponse.Container.Id.Returns("Items");
        containerResponse.StatusCode.Returns(created ? HttpStatusCode.Created : HttpStatusCode.OK);
        database.CreateContainerIfNotExistsAsync(Arg.Any<ContainerProperties>(), Arg.Any<ThroughputProperties>(), Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(containerResponse);
    }

    private static ArmCosmosContext ConfigureArm(int status = 200)
    {
        var account = Substitute.For<CosmosDBAccountResource>();
        account.Data.Returns(System.ClientModel.Primitives.ModelReaderWriter.Read<CosmosDBAccountData>(
            BinaryData.FromString("""{"location":"westus"}"""))!);
        var databases = Substitute.For<CosmosDBSqlDatabaseCollection>();
        account.GetCosmosDBSqlDatabases().Returns(databases);
        var database = Substitute.For<CosmosDBSqlDatabaseResource>();
        database.Data.Returns(System.ClientModel.Primitives.ModelReaderWriter.Read<CosmosDBSqlDatabaseData>(
            BinaryData.FromString("""{"properties":{"resource":{"id":"db"}}}"""))!);
        var databaseOperation = Substitute.For<ArmOperation<CosmosDBSqlDatabaseResource>>();
        databaseOperation.Value.Returns(database);
        var response = Substitute.For<Response>();
        response.Status.Returns(status);
        databaseOperation.GetRawResponse().Returns(response);
        databases.CreateOrUpdateAsync(WaitUntil.Completed, "db", Arg.Any<CosmosDBSqlDatabaseCreateOrUpdateContent>(), Arg.Any<CancellationToken>())
            .Returns(databaseOperation);
        databases.GetIfExistsAsync("db", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<NullableResponse<CosmosDBSqlDatabaseResource>>(Response.FromValue(database, Substitute.For<Response>())));
        var containers = Substitute.For<CosmosDBSqlContainerCollection>();
        database.GetCosmosDBSqlContainers().Returns(containers);
        var container = Substitute.For<CosmosDBSqlContainerResource>();
        container.Data.Returns(System.ClientModel.Primitives.ModelReaderWriter.Read<CosmosDBSqlContainerData>(
            BinaryData.FromString("""{"properties":{"resource":{"id":"Items"}}}"""))!);
        var containerOperation = Substitute.For<ArmOperation<CosmosDBSqlContainerResource>>();
        containerOperation.Value.Returns(container);
        containerOperation.GetRawResponse().Returns(response);
        containers.CreateOrUpdateAsync(WaitUntil.Completed, "Items", Arg.Any<CosmosDBSqlContainerCreateOrUpdateContent>(), Arg.Any<CancellationToken>())
            .Returns(containerOperation);
        var id = new ResourceIdentifier("/subscriptions/s/resourceGroups/rg/providers/Microsoft.DocumentDB/databaseAccounts/a");
        return new ArmCosmosContext(Substitute.For<ArmClient>(), id, "s", "rg", "a", new Uri("https://a.documents.azure.com"), account);
    }
}
