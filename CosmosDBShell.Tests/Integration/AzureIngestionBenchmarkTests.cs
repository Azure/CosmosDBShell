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
        var creationCompleted = false;
        Exception? failure = null;
        try
        {
            creationAttempted = true;

            // Cancellation must count as failure here: a canceled ARM create may still complete, so
            // only a create that returned without error or cancellation is treated as completed.
            var database = await Shell.ExecuteCommandAsync($"mkdb {databaseName} --scale m --ru 400", token, cancellationIsError: true);
            Assert.False(database.IsError, FormatError(database));
            token.ThrowIfCancellationRequested();
            creationCompleted = true;
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
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            File.Delete(inputPath);
            if (creationAttempted)
            {
                var cleanupProblem = await this.DeleteDatabaseAsync(databaseName, creationCompleted);
                if (cleanupProblem is not null)
                {
                    // Keep the benchmark's own failure, if any, as the inner exception.
                    throw new InvalidOperationException(cleanupProblem, failure);
                }
            }
        }
    }

    /// <summary>
    /// Deletes the benchmark database so provisioned throughput stops billing, and returns a problem
    /// description when deletion cannot be confirmed. A completed create is deleted with retries. After
    /// a failed or canceled create, the outcome is unknown: the database is watched and deleted if it
    /// appears, but absence is never reported as success because the create could still complete.
    /// Cancellation of cleanup commands counts as failure so incomplete cleanup is not hidden.
    /// </summary>
    private async Task<string?> DeleteDatabaseAsync(string databaseName, bool creationCompleted)
    {
        var attempts = creationCompleted ? 12 : 30;
        var notFound = MessageService.GetString(
            "command-rmdb-error-database_not_found",
            new Dictionary<string, object> { { "db", databaseName } });
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var token = cleanupTimeout.Token;
        var lastError = "no attempt completed";
        var manualCheck = $"Check the account for {databaseName} and delete it manually to stop throughput charges.";

        // rmdb is rejected inside a container, so return to the account root first.
        var root = await Shell.ExecuteCommandAsync("cd /", token, cancellationIsError: true);
        if (root.IsError)
        {
            return $"Cleanup could not navigate to the account root ({FormatError(root)}). {manualCheck}";
        }

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var probe = await Shell.ExecuteCommandAsync($"rmdb {databaseName} --dry-run", token, cancellationIsError: true);
            if (probe is ErrorCommandState { Exception.Message: var probeMessage } && probeMessage == notFound)
            {
                if (creationCompleted)
                {
                    // The database existed, so not found means it is already deleted.
                    return null;
                }

                lastError = "not found yet; the canceled or failed create may still complete";
            }
            else if (probe.IsError)
            {
                lastError = FormatError(probe);
            }
            else
            {
                var delete = await Shell.ExecuteCommandAsync($"rmdb {databaseName} true", token, cancellationIsError: true);
                if (!delete.IsError)
                {
                    return null;
                }

                // A create that is still running can reject the delete; retry after it settles.
                lastError = FormatError(delete);
            }

            await Task.Delay(TimeSpan.FromSeconds(10), token);
        }

        return creationCompleted
            ? $"Cleanup could not confirm that {databaseName} was deleted ({lastError}). {manualCheck}"
            : $"Cleanup is unconfirmed: database creation did not complete, and {databaseName} did not appear within {attempts * 10} seconds ({lastError}). {manualCheck}";
    }
}
