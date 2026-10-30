// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Microsoft.Azure.Cosmos.Linq;

public class IngestionBenchmarkTests : ConnectedEmulatorTestBase
{
    private const string OutputVariable = "COSMOSDB_SHELL_INGESTION_BENCHMARK_CSV";

    public override async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OutputVariable)),
            $"Set {OutputVariable} to a results CSV path to run the opt-in emulator benchmark.");
        await base.InitializeAsync();
    }

    [Fact]
    public async Task MeasureImportAndBulk()
    {
        var token = TestContext.Current.CancellationToken;
        var outputPath = Path.GetFullPath(Environment.GetEnvironmentVariable(OutputVariable)!);
        using var results = new StreamWriter(outputPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        await results.WriteLineAsync(IngestionBenchmarkWorkload.CsvHeader);
        var databaseName = $"IngestionBenchmark_{Guid.NewGuid():N}";
        CreatedDatabases.Add(databaseName);
        var database = (await this.CosmosClient.CreateDatabaseAsync(databaseName, cancellationToken: token)).Database;
        Shell.Options = new Program.CosmosShellOptions { Output = "json" };
        Shell.IsInteractiveSession = () => false;
        var inputPath = Path.GetTempFileName();
        try
        {
            foreach (var size in new[] { 1_000, 10_000, 100_000 })
            {
                foreach (var command in new[] { "import", "bulk" })
                {
                    await IngestionBenchmarkWorkload.WriteInputAsync(inputPath, size, command == "bulk", caseId: null, token);
                    foreach (var concurrency in new[] { 1, 16, 32 })
                    {
                        var containerName = $"items-{command}-{size}-{concurrency}";
                        var container = (await database.CreateContainerAsync(
                            containerName, "/pk", throughput: 10_000, cancellationToken: token)).Container;
                        try
                        {
                            var navigation = await this.ExecuteAsync($"cd /{databaseName}/{containerName}");
                            Assert.False(navigation.IsError, FormatError(navigation));
                            var text = command == "import"
                                ? $"import {ShellQuotedPath(inputPath)} --concurrency={concurrency}"
                                : $"bulk run {ShellQuotedPath(inputPath)} --concurrency={concurrency} --yes";
                            var watch = Stopwatch.StartNew();
                            // Use the parsed command runner so timings stay comparable with docs/ingestion.md.
                            var state = await Shell.RunCommandAsync(new CommandState(), text, token);
                            watch.Stop();
                            Assert.False(state.IsError, FormatError(state));
                            var result = Assert.IsType<ShellJson>(state.Result).Value;
                            Assert.Equal(size, result.GetProperty(command == "import" ? "imported" : "succeeded").GetInt64());
                            Assert.Equal(0, result.GetProperty("failed").GetInt64());
                            var verified = (await container.GetItemLinqQueryable<JsonElement>().CountAsync(token)).Resource;
                            Assert.Equal(size, verified);
                            await results.WriteLineAsync(IngestionBenchmarkWorkload.FormatRow(
                                command, size, new FileInfo(inputPath).Length, concurrency, watch.Elapsed, result, verified));
                        }
                        finally
                        {
                            await container.DeleteContainerAsync(cancellationToken: token);
                        }
                    }
                }
            }
        }
        finally
        {
            File.Delete(inputPath);
        }
    }
}
