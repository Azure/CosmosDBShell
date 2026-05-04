// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;
using System.Threading;

using Azure.Data.Cosmos.Shell.Core;

using Xunit;

public class QueryCommandTests : EmulatorFixtureTestBase
{
    private readonly string seedPrefix = $"qtest-{Guid.NewGuid():N}";

    public QueryCommandTests(EmulatorDatabaseFixture fixture)
        : base(fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        // Navigate to the test container
        await ExecuteAsync("cd");
        await ExecuteAsync($"cd {Fixture.DatabaseName}/{Fixture.ContainerName}");

        // Seed test items
        for (int i = 1; i <= 5; i++)
        {
            var json = JsonSerializer.Serialize(new
            {
                id = this.GetSeedItemId(i),
                name = $"item-{i}",
                category = i <= 3 ? "alpha" : "beta",
                score = i * 10,
            });

            var state = await ExecuteAsync($"mkitem '{json}'");
            Assert.False(state.IsError, FormatError(state));
        }
    }

    [Fact]
    public async Task Query_SelectAll_ReturnsResults()
    {
        var output = await ExecuteWithOutputAsync($"query \"SELECT * FROM c WHERE STARTSWITH(c.id, '{this.seedPrefix}-')\"");

        var doc = JsonDocument.Parse(output);
        var items = doc.RootElement.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 5);
    }

    [Fact]
    public async Task Query_WithFilter_ReturnsFilteredResults()
    {
        var output = await ExecuteWithOutputAsync($"query \"SELECT * FROM c WHERE c.category = 'alpha' AND STARTSWITH(c.id, '{this.seedPrefix}-')\"");

        var doc = JsonDocument.Parse(output);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
    }

    [Fact]
    public async Task Query_SelectSpecificFields_ReturnsProjection()
    {
        var output = await ExecuteWithOutputAsync($"query \"SELECT c.id, c.name FROM c WHERE c.id = '{this.GetSeedItemId(1)}'\"");

        var doc = JsonDocument.Parse(output);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());

        var item = items[0];
        Assert.Equal(this.GetSeedItemId(1), item.GetProperty("id").GetString());
        Assert.Equal("item-1", item.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Query_MaxOption_LimitsResults()
    {
        var output = await ExecuteWithOutputAsync($"query \"SELECT * FROM c WHERE STARTSWITH(c.id, '{this.seedPrefix}-')\" -max 2");

        var doc = JsonDocument.Parse(output);
        var items = doc.RootElement.GetProperty("items");
        Assert.True(items.GetArrayLength() <= 2);
    }

    [Fact]
    public async Task Query_MetricsDisplay_ReturnsItemsAndRendersMetrics()
    {
        var output = await ExecuteWithOutputAsync($"query \"SELECT * FROM c WHERE c.id = '{this.GetSeedItemId(1)}'\" -metrics:Display");

        // Metrics=Display puts plain items in the result (metrics tables go to AnsiConsole)
        var doc = JsonDocument.Parse(output);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(this.GetSeedItemId(1), items[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Query_MetricsFile_ContainsMetricFields()
    {
        var output = await ExecuteWithOutputAsync($"query \"SELECT * FROM c WHERE c.id = '{this.GetSeedItemId(1)}'\" -metrics:File");

        var doc = JsonDocument.Parse(output);
        var json = doc.RootElement;

        // Verify top-level metric properties exist
        Assert.True(json.TryGetProperty("requestCharge", out var charge), "Expected requestCharge in result");
        Assert.True(charge.GetDouble() >= 0, "requestCharge should be non-negative");

        Assert.True(json.TryGetProperty("queryMetrics", out var metrics), "Expected queryMetrics in result");
        Assert.Equal(JsonValueKind.Array, metrics.ValueKind);
        Assert.True(metrics.GetArrayLength() > 0, "queryMetrics should contain entries");

        // Verify individual metric entries have expected structure
        var firstMetric = metrics[0];
        Assert.True(firstMetric.TryGetProperty("metric", out _), "Each metric should have a 'metric' name");
        Assert.True(firstMetric.TryGetProperty("value", out _), "Each metric should have a 'value'");

        Assert.True(json.TryGetProperty("indexMetrics", out _), "Expected indexMetrics in result");

        // Verify documents are included alongside metrics
        Assert.True(json.TryGetProperty("documents", out var docs), "Expected documents in result");
        Assert.True(docs.GetArrayLength() > 0, "documents should contain the queried item");
    }

    [Fact]
    public async Task Query_MetricsFile_Csv_WritesMetricsCsvFile()
    {
        var outputFile = CreateTempFile(".csv");
        var metricsFile = Path.ChangeExtension(outputFile, "metrics.csv");
        Shell.StdOutRedirect = outputFile;
        try
        {
            var state = await ExecuteAsync($"query \"SELECT * FROM c WHERE c.id = '{this.GetSeedItemId(1)}'\" -metrics:File -f:csv");
            Assert.False(state.IsError, FormatError(state));

            // Metrics=File with csv format writes a separate .metrics.csv file
            Assert.True(File.Exists(metricsFile), $"Expected metrics file at {metricsFile}");

            var metricsContent = await File.ReadAllTextAsync(metricsFile, TestContext.Current.CancellationToken);
            Assert.Contains("Request Charge", metricsContent);
            Assert.Contains("Retrieved document count", metricsContent);
        }
        finally
        {
            Shell.StdOutRedirect = null;
            if (File.Exists(metricsFile))
            {
                try
                {
                    File.Delete(metricsFile);
                }
                catch
                {
                    // Best-effort cleanup
                }
            }
        }
    }

    [Fact]
    public async Task Query_FormatCsv_Succeeds()
    {
        var state = await ExecuteAsync($"query \"SELECT * FROM c WHERE c.id = '{this.GetSeedItemId(1)}'\" -f:csv");
        var errorMsg = FormatError(state);

        Assert.False(state.IsError, errorMsg);
    }

    [Fact]
    public async Task Query_DatabaseAndContainerOptions_Succeeds()
    {
        // Navigate to the connected root so we're not inside a container
        await ExecuteAsync("cd");

        var output = await ExecuteWithOutputAsync(
            $"query \"SELECT * FROM c WHERE c.id = '{this.GetSeedItemId(1)}'\" --database:{Fixture.DatabaseName} --container:{Fixture.ContainerName}");

        var doc = JsonDocument.Parse(output);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());

        // Navigate back for other tests
        await ExecuteAsync($"cd {Fixture.DatabaseName}/{Fixture.ContainerName}");
    }

    [Fact]
    public async Task Query_EmptyQuery_ReturnsError()
    {
        var state = await ExecuteAsync("query \"\"");

        Assert.True(state.IsError);
    }

    [Fact]
    public async Task Query_InvalidSql_ReturnsError()
    {
        var state = await ExecuteAsync("query \"NOT VALID SQL AT ALL\"");

        Assert.True(state.IsError);
    }

    [Fact]
    public async Task Query_NoResults_ReturnsEmptyItems()
    {
        var output = await ExecuteWithOutputAsync("query \"SELECT * FROM c WHERE c.id = 'nonexistent-id-12345'\"");

        var doc = JsonDocument.Parse(output);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(0, items.GetArrayLength());
    }

    [Fact]
    public async Task Query_CountAggregate_ReturnsCount()
    {
        var output = await ExecuteWithOutputAsync($"query \"SELECT VALUE COUNT(1) FROM c WHERE STARTSWITH(c.id, '{this.seedPrefix}-')\"");

        var doc = JsonDocument.Parse(output);
        var items = doc.RootElement.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0);

        // COUNT results may be returned as a raw number or wrapped in an object
        var first = items[0];
        var rawText = first.GetRawText();
        Assert.True(int.TryParse(rawText, out var count) ? count >= 5 : rawText.Contains('5'),
            $"Expected count >= 5 but got: {rawText}");
    }

    [Fact]
    public async Task Query_OrderBy_ReturnsOrdered()
    {
        var output = await ExecuteWithOutputAsync($"query \"SELECT c.id FROM c WHERE STARTSWITH(c.id, '{this.seedPrefix}-') ORDER BY c.score DESC\"");

        var doc = JsonDocument.Parse(output);
        var items = doc.RootElement.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 5);

        // First item should be the one with highest score (qtest-5)
        Assert.Equal(this.GetSeedItemId(5), items[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Query_NotConnected_ReturnsError()
    {
        var shell = ShellInterpreter.CreateInstance();
        try
        {
            var state = await shell.ExecuteCommandAsync(
                "query \"SELECT * FROM c\"", CancellationToken.None);
            Assert.True(state.IsError);
        }
        finally
        {
            shell.Dispose();
        }
    }

    private string GetSeedItemId(int index)
    {
        return $"{this.seedPrefix}-{index}";
    }
}
