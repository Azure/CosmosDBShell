// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Net;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.States;
using Microsoft.Azure.Cosmos;
using NSubstitute;

[Collection(ConsoleOutputTestCollection.Name)]
public class MachineModeWriteErrorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Import_PreservesLineStatusAndServiceDiagnostics(bool upsert, bool quiet)
    {
        var (shell, container) = CreateShell(quiet);
        using (shell)
        {
            ConfigureWrites(container);
            var path = Path.Join(Path.GetTempPath(), $"cosmosshell-import-errors-{Guid.NewGuid():N}.jsonl");
            try
            {
                await File.WriteAllTextAsync(
                    path,
                    "{\"id\":\"bad-status\",\"pk\":\"x\"}\n{\"id\":\"denied\",\"pk\":\"x\"}\n",
                    TestContext.Current.CancellationToken);
                var command = $"import '{path}' --continue-on-error --concurrency=2"
                    + (upsert ? " --mode=upsert" : string.Empty);
                var error = await CaptureMachineErrorAsync(shell, command);

                Assert.Contains("Line 1", error);
                Assert.Contains("BadRequest", error);
                Assert.Contains("Line 2", error);
                Assert.Contains("Forbidden", error);
                Assert.Contains("bulk service denied", error);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Import_ParseFailure_PreservesEarlierWriteDiagnostics()
    {
        var (shell, container) = CreateShell(quiet: true);
        using (shell)
        {
            ConfigureWrites(container);
            var path = Path.Join(Path.GetTempPath(), $"cosmosshell-import-parse-errors-{Guid.NewGuid():N}.jsonl");
            try
            {
                await File.WriteAllTextAsync(
                    path,
                    "{\"id\":\"bad-status\",\"pk\":\"x\"}\nnot-json\n",
                    TestContext.Current.CancellationToken);
                var error = await CaptureMachineErrorAsync(shell, $"import '{path}' --continue-on-error --concurrency=2");

                Assert.Contains("Line 1", error);
                Assert.Contains("BadRequest", error);
                Assert.Contains("2", error);
                Assert.Contains("JSON", error, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Import_ArrayParseFailure_PreservesEarlierWriteDiagnostics()
    {
        var (shell, container) = CreateShell(quiet: true);
        using (shell)
        {
            ConfigureWrites(container);
            var path = Path.Join(Path.GetTempPath(), $"cosmosshell-import-array-errors-{Guid.NewGuid():N}.json");
            try
            {
                await File.WriteAllTextAsync(
                    path,
                    "[{\"id\":\"bad-status\",\"pk\":\"x\"},{\"id\":\"denied\",\"pk\":\"x\"},",
                    TestContext.Current.CancellationToken);
                var error = await CaptureMachineErrorAsync(shell, $"import '{path}' --continue-on-error --concurrency=2");

                Assert.Contains("Line 1", error);
                Assert.Contains("BadRequest", error);
                Assert.Contains("Line 2", error);
                Assert.Contains("Forbidden", error);
                Assert.Contains("bulk service denied", error);
                Assert.Contains("Expected start of a property name or value", error);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Import_TooManyFailures_BoundsDiagnosticsAndReportsOmittedCount()
    {
        var (shell, container) = CreateShell(quiet: true);
        using (shell)
        {
            ConfigureWrites(container);
            var path = Path.Join(Path.GetTempPath(), $"cosmosshell-import-many-errors-{Guid.NewGuid():N}.jsonl");
            try
            {
                var records = Enumerable.Repeat("{\"id\":\"denied\",\"pk\":\"x\"}", BulkWriteDiagnostics.MaximumCount + 3);
                await File.WriteAllLinesAsync(path, records, TestContext.Current.CancellationToken);
                var error = await CaptureMachineErrorAsync(shell, $"import '{path}' --continue-on-error --concurrency=16");

                Assert.Equal(
                    BulkWriteDiagnostics.MaximumCount,
                    error.Split("Line ", StringSplitOptions.None).Length - 1);
                Assert.Contains("3 additional item diagnostics omitted", error);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void BulkWriteDiagnostics_TruncatesLongMessages()
    {
        var diagnostics = new BulkWriteDiagnostics();
        diagnostics.Add(new string('x', BulkWriteDiagnostics.MaximumLength + 100));

        var message = diagnostics.GetMessage();

        Assert.True(message.Length <= BulkWriteDiagnostics.MaximumLength);
        Assert.Contains("[diagnostic truncated]", message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MakeItem_Bulk_PreservesStatusAndServiceDiagnostics(bool upsert, bool quiet)
    {
        var (shell, container) = CreateShell(quiet);
        using (shell)
        {
            ConfigureWrites(container);
            var command = "mkitem '[{\"id\":\"bad-status\",\"pk\":\"x\"},{\"id\":\"denied\",\"pk\":\"x\"}]'"
                + (upsert ? " --force" : string.Empty);
            var error = await CaptureMachineErrorAsync(shell, command);

            Assert.Contains("Item 1", error);
            Assert.Contains("BadRequest", error);
            Assert.Contains("Item 2", error);
            Assert.Contains("Forbidden", error);
            Assert.Contains("bulk service denied", error);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MakeItem_SingleFailedStatus_ReportsStructuredError(bool upsert)
    {
        var (shell, container) = CreateShell(quiet: true);
        using (shell)
        {
            ConfigureWrites(container);
            var command = "mkitem '{\"id\":\"bad-status\",\"pk\":\"x\"}'" + (upsert ? " --force" : string.Empty);
            Assert.Contains("BadRequest", await CaptureMachineErrorAsync(shell, command));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replace_Bulk_PreservesValidationStatusAndServiceDiagnostics(bool quiet)
    {
        var (shell, container) = CreateShell(quiet);
        using (shell)
        {
            ConfigureWrites(container);
            var error = await CaptureMachineErrorAsync(
                shell,
                "replace '[{\"pk\":\"x\"},{\"id\":\"bad-status\",\"pk\":\"x\"},{\"id\":\"denied\",\"pk\":\"x\"}]'");

            Assert.Contains("Item 1", error);
            Assert.Contains("non-empty 'id'", error);
            Assert.Contains("Item 2", error);
            Assert.Contains("BadRequest", error);
            Assert.Contains("Item 3", error);
            Assert.Contains("Forbidden", error);
            Assert.Contains("bulk service denied", error);
        }
    }

    [Theory]
    [InlineData("mkitem")]
    [InlineData("replace")]
    public async Task BulkWrite_TooManyFailures_BoundsDiagnosticsAndReportsOmittedCount(string commandName)
    {
        var (shell, container) = CreateShell(quiet: true);
        using (shell)
        {
            ConfigureWrites(container);
            var items = string.Join(',', Enumerable.Repeat("{\"id\":\"denied\",\"pk\":\"x\"}", BulkWriteDiagnostics.MaximumCount + 3));
            var error = await CaptureMachineErrorAsync(shell, $"{commandName} '[{items}]'");

            Assert.Equal(
                BulkWriteDiagnostics.MaximumCount,
                error.Split("Item ", StringSplitOptions.None).Length - 1);
            Assert.Contains("3 additional item diagnostics omitted", error);
        }
    }

    private static (ShellInterpreter Shell, Container Container) CreateShell(bool quiet)
    {
        var client = Substitute.For<CosmosClient>();
        client.Endpoint.Returns(new Uri("https://account.documents.azure.com:443/"));
        var database = Substitute.For<Database>();
        var container = Substitute.For<Container>();
        client.GetDatabase("db").Returns(database);
        database.GetContainer("items").Returns(container);
        var databaseResponse = Substitute.For<DatabaseResponse>();
        databaseResponse.StatusCode.Returns(HttpStatusCode.OK);
        database.ReadAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(databaseResponse));
        var containerResponse = Substitute.For<ContainerResponse>();
        containerResponse.StatusCode.Returns(HttpStatusCode.OK);
        containerResponse.Resource.Returns(new ContainerProperties("items", "/pk"));
        container.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(containerResponse));

        var shell = ShellInterpreter.CreateInstance();
        shell.Options = new Program.CosmosShellOptions { Output = "json", Quiet = quiet };
        shell.State = new ContainerState("items", "db", client);
        return (shell, container);
    }

    private static void ConfigureWrites(Container container)
    {
        Task<ItemResponse<JsonElement>> Write(JsonElement item)
        {
            if (item.GetProperty("id").GetString() == "bad-status")
            {
                var response = Substitute.For<ItemResponse<JsonElement>>();
                response.StatusCode.Returns(HttpStatusCode.BadRequest);
                return Task.FromResult(response);
            }

            return Task.FromException<ItemResponse<JsonElement>>(
                new CosmosException("bulk service denied", HttpStatusCode.Forbidden, 0, "test", 0));
        }

        container.CreateItemAsync(Arg.Any<JsonElement>(), Arg.Any<PartitionKey?>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => Write(call.ArgAt<JsonElement>(0)));
        container.UpsertItemAsync(Arg.Any<JsonElement>(), Arg.Any<PartitionKey?>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => Write(call.ArgAt<JsonElement>(0)));
        container.ReplaceItemAsync(Arg.Any<JsonElement>(), Arg.Any<string>(), Arg.Any<PartitionKey?>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => Write(call.ArgAt<JsonElement>(0)));

        static Task<ResponseMessage> WriteStream(Stream body)
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.GetProperty("id").GetString() == "bad-status")
            {
                return Task.FromResult(new ResponseMessage(HttpStatusCode.BadRequest));
            }

            return Task.FromException<ResponseMessage>(
                new CosmosException("bulk service denied", HttpStatusCode.Forbidden, 0, "test", 0));
        }

        container.CreateItemStreamAsync(Arg.Any<Stream>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => WriteStream(call.ArgAt<Stream>(0)));
        container.UpsertItemStreamAsync(Arg.Any<Stream>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => WriteStream(call.ArgAt<Stream>(0)));
        container.ReplaceItemStreamAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => WriteStream(call.ArgAt<Stream>(0)));
    }

    private static async Task<string> CaptureMachineErrorAsync(ShellInterpreter shell, string command)
    {
        var savedOut = Console.Out;
        var savedError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var state = await shell.ExecuteCommandAsync(command, TestContext.Current.CancellationToken);

            Assert.True(state.IsError);
            Assert.Empty(stdout.ToString());
            var line = Assert.Single(stderr.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
            using var json = JsonDocument.Parse(line);
            Assert.Equal("error", json.RootElement.GetProperty("status").GetString());
            Assert.DoesNotContain("\u001b", line, StringComparison.Ordinal);
            return Assert.IsType<string>(json.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedError);
        }
    }
}
