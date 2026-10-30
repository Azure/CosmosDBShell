// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;
using Azure.Identity;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Opt-in ingestion benchmark against a real Azure Cosmos DB account, using the same workload and
/// matrix as <see cref="IngestionBenchmarkTests"/>. It signs in with the Azure CLI identity, needs a
/// data-plane write role plus ARM rights to create and delete databases, and incurs provisioned
/// throughput charges. It creates one temporary database and container and always deletes them.
/// </summary>
[Trait("Category", "LiveAzure")]
public class AzureIngestionBenchmarkTests : IntegrationTestBase
{
    private const string EndpointVariable = "COSMOSDB_SHELL_AZURE_BENCHMARK_ENDPOINT";
    private const string SubscriptionVariable = "COSMOSDB_SHELL_AZURE_BENCHMARK_SUBSCRIPTION";
    private const string ResourceGroupVariable = "COSMOSDB_SHELL_AZURE_BENCHMARK_RESOURCE_GROUP";
    private const string OutputVariable = "COSMOSDB_SHELL_AZURE_BENCHMARK_CSV";
    private const string ThroughputVariable = "COSMOSDB_SHELL_AZURE_BENCHMARK_RU";

    [Fact]
    public async Task MeasureImportAndBulkOnAzure()
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointVariable);
        var subscription = Environment.GetEnvironmentVariable(SubscriptionVariable);
        var resourceGroup = Environment.GetEnvironmentVariable(ResourceGroupVariable);
        var output = Environment.GetEnvironmentVariable(OutputVariable);
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(subscription)
                || string.IsNullOrWhiteSpace(resourceGroup) || string.IsNullOrWhiteSpace(output),
            $"Set {EndpointVariable}, {SubscriptionVariable}, {ResourceGroupVariable}, and {OutputVariable} to run the opt-in Azure benchmark.");
        var throughput = int.TryParse(Environment.GetEnvironmentVariable(ThroughputVariable), out var ru) ? ru : 4000;

        var token = TestContext.Current.CancellationToken;
        Shell.Options = new Program.CosmosShellOptions { Output = "json" };
        Shell.IsInteractiveSession = () => false;
        await Shell.ConnectAsync(
            endpoint!,
            credentialMethod: CredentialMethod.AzureCli,
            subscriptionId: subscription,
            resourceGroupName: resourceGroup,
            token: token);

        var databaseName = $"IngestionBenchmark_{Guid.NewGuid():N}";
        var inputPath = Path.GetTempFileName();

        // Set before mkdb: a failed or canceled create can still have been accepted by Azure.
        var creationAttempted = false;
        try
        {
            creationAttempted = true;
            var database = await this.RunScriptAsync($"mkdb {databaseName} --scale m --ru 400");
            Assert.False(database.IsError, FormatError(database));
            var container = await this.RunScriptAsync($"mkcon items /pk --database {databaseName} --scale m --ru {throughput}");
            Assert.False(container.IsError, FormatError(container));
            var navigation = await this.RunScriptAsync($"cd /{databaseName}/items");
            Assert.False(navigation.IsError, FormatError(navigation));

            using var verifier = new CosmosClient(endpoint, new AzureCliCredential());
            var items = verifier.GetContainer(databaseName, "items");
            using var results = new StreamWriter(Path.GetFullPath(output!), append: false, new UTF8Encoding(false)) { AutoFlush = true };
            await results.WriteLineAsync(IngestionBenchmarkWorkload.CsvHeader);
            foreach (var size in IngestionBenchmarkWorkload.Sizes)
            {
                foreach (var command in IngestionBenchmarkWorkload.Commands)
                {
                    foreach (var concurrency in IngestionBenchmarkWorkload.Concurrencies)
                    {
                        // Each case writes uniquely prefixed IDs into the shared container.
                        var caseId = $"{command}-{size}-{concurrency}";
                        await IngestionBenchmarkWorkload.WriteInputAsync(inputPath, size, command == "bulk", caseId, token);
                        var text = command == "import"
                            ? $"import {ShellQuotedPath(inputPath)} --concurrency={concurrency}"
                            : $"bulk run {ShellQuotedPath(inputPath)} --concurrency={concurrency} --yes";
                        var watch = Stopwatch.StartNew();
                        var state = await Shell.RunCommandAsync(new CommandState(), text, token);
                        watch.Stop();
                        Assert.False(state.IsError, FormatError(state));
                        var result = Assert.IsType<ShellJson>(state.Result).Value;
                        Assert.Equal(size, result.GetProperty(command == "import" ? "imported" : "succeeded").GetInt64());
                        Assert.Equal(0, result.GetProperty("failed").GetInt64());

                        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.caseId = @caseId").WithParameter("@caseId", caseId);
                        using var iterator = items.GetItemQueryIterator<long>(query);
                        long verified = 0;
                        while (iterator.HasMoreResults)
                        {
                            verified += (await iterator.ReadNextAsync(token)).Sum();
                        }

                        Assert.Equal(size, verified);
                        await results.WriteLineAsync(IngestionBenchmarkWorkload.FormatRow(
                            command, size, new FileInfo(inputPath).Length, concurrency, watch.Elapsed, result, verified));
                    }
                }
            }
        }
        finally
        {
            File.Delete(inputPath);
            if (creationAttempted)
            {
                await this.DeleteDatabaseAsync(databaseName);
            }
        }
    }

    /// <summary>
    /// Deletes the benchmark database so provisioned throughput stops billing. Retries cover a create
    /// that is still finishing; absence is accepted only after several consecutive not-found checks.
    /// Cancellation counts as failure so incomplete cleanup is reported instead of passing silently.
    /// </summary>
    private async Task DeleteDatabaseAsync(string databaseName)
    {
        const int Attempts = 12;
        const int RequiredAbsentChecks = 3;
        var notFound = MessageService.GetString(
            "command-rmdb-error-database_not_found",
            new Dictionary<string, object> { { "db", databaseName } });
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var token = cleanupTimeout.Token;
        var lastError = "no attempt completed";
        var absentChecks = 0;

        // rmdb is rejected inside a container, so return to the account root first.
        var root = await Shell.ExecuteCommandAsync("cd /", token, cancellationIsError: true);
        Assert.False(root.IsError, $"Cleanup of {databaseName} could not navigate to the account root: {FormatError(root)}. Delete it manually.");

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var probe = await Shell.ExecuteCommandAsync($"rmdb {databaseName} --dry-run", token, cancellationIsError: true);
            if (probe is ErrorCommandState { Exception.Message: var probeMessage } && probeMessage == notFound)
            {
                if (++absentChecks >= RequiredAbsentChecks)
                {
                    return;
                }
            }
            else if (probe.IsError)
            {
                absentChecks = 0;
                lastError = FormatError(probe);
            }
            else
            {
                absentChecks = 0;
                var delete = await Shell.ExecuteCommandAsync($"rmdb {databaseName} true", token, cancellationIsError: true);
                if (!delete.IsError)
                {
                    return;
                }

                // A create that is still running can reject the delete; retry after it settles.
                lastError = FormatError(delete);
            }

            await Task.Delay(TimeSpan.FromSeconds(10), token);
        }

        Assert.Fail($"Cleanup could not confirm that {databaseName} was deleted ({lastError}). Delete it manually to stop throughput charges.");
    }
}
