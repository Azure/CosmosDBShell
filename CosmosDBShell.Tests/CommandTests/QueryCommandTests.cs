// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Microsoft.Azure.Cosmos;

public class QueryCommandTests
{
    private class TestServerSideMetrics : ServerSideMetrics
    {
        public override long RetrievedDocumentCount { get; } = 0;

        public override long RetrievedDocumentSize { get; } = 0;

        public override long OutputDocumentCount { get; } = 0;

        public override long OutputDocumentSize { get; } = 0;

        public override double IndexHitRatio { get; } = 0;

        public override TimeSpan IndexLookupTime { get; } = TimeSpan.Zero;

        public override TimeSpan DocumentLoadTime { get; } = TimeSpan.Zero;

        public override TimeSpan DocumentWriteTime { get; } = TimeSpan.Zero;

        public override TimeSpan RuntimeExecutionTime { get; } = TimeSpan.Zero;

        public override TimeSpan VMExecutionTime { get; } = TimeSpan.Zero;

        public override TimeSpan QueryPreparationTime { get; } = TimeSpan.Zero;

        public override TimeSpan TotalTime { get; } = TimeSpan.Zero;

        public TestServerSideMetrics()
        {
        }

        public TestServerSideMetrics(
            long retrievedDocumentCount,
            long retrievedDocumentSize,
            long outputDocumentCount,
            long outputDocumentSize,
            double indexHitRatio,
            TimeSpan indexLookupTime,
            TimeSpan documentLoadTime,
            TimeSpan documentWriteTime,
            TimeSpan runtimeExecutionTime,
            TimeSpan vmExecutionTime,
            TimeSpan queryPreparationTime,
            TimeSpan totalTime)
        {
            this.RetrievedDocumentCount = retrievedDocumentCount;
            this.RetrievedDocumentSize = retrievedDocumentSize;
            this.OutputDocumentCount = outputDocumentCount;
            this.OutputDocumentSize = outputDocumentSize;
            this.IndexHitRatio = indexHitRatio;
            this.IndexLookupTime = indexLookupTime;
            this.DocumentLoadTime = documentLoadTime;
            this.DocumentWriteTime = documentWriteTime;
            this.RuntimeExecutionTime = runtimeExecutionTime;
            this.VMExecutionTime = vmExecutionTime;
            this.QueryPreparationTime = queryPreparationTime;
            this.TotalTime = totalTime;
        }
    }

    [Fact]
    public void CollectDocuments_AppendsDocumentsAcrossPages()
    {
        var firstPage = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "1" },
            new { id = "2" },
        });
        var secondPage = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "3" },
            new { id = "4" },
        });

        var documents = QueryCommand.CollectDocuments([], firstPage, null);
        documents = QueryCommand.CollectDocuments(documents, secondPage, null);

        Assert.Collection(
            documents,
            item => Assert.Equal("1", item.GetProperty("id").GetString()),
            item => Assert.Equal("2", item.GetProperty("id").GetString()),
            item => Assert.Equal("3", item.GetProperty("id").GetString()),
            item => Assert.Equal("4", item.GetProperty("id").GetString()));
    }

    [Fact]
    public void CollectDocuments_EnforcesGlobalMaxAcrossPages()
    {
        var firstPage = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "1" },
            new { id = "2" },
        });
        var secondPage = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "3" },
            new { id = "4" },
        });

        var documents = QueryCommand.CollectDocuments([], firstPage, 3);
        documents = QueryCommand.CollectDocuments(documents, secondPage, 3);

        Assert.Collection(
            documents,
            item => Assert.Equal("1", item.GetProperty("id").GetString()),
            item => Assert.Equal("2", item.GetProperty("id").GetString()),
            item => Assert.Equal("3", item.GetProperty("id").GetString()));
    }

    [Fact]
    public void BuildMetrics_NullCumulative_ReturnsZeroDefaults()
    {
        var metrics = QueryCommand.BuildMetrics(0, null);

        Assert.Equal(13, metrics.Count);
        Assert.All(metrics, entry =>
        {
            Assert.Contains("value", entry.Keys);
            var value = entry["value"];
            TestContext.Current.TestOutputHelper?.WriteLine($"{entry["metric"]}: {value}");
            if ((string)entry["metric"] == "Request Charge")
            {
                Assert.Equal(0.0, value);
            }
            else
            {
                Assert.Null(value);
            }
        });

        // All cumulative-derived metrics (all except Request Charge) should show "N/A"
        var cumulativeMetrics = metrics.Where(m => (string)m["metric"] != "Request Charge").ToList();
        Assert.All(cumulativeMetrics, entry =>
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"{entry["metric"]}: {entry["formattedValue"]}");
            Assert.Equal("N/A", entry["formattedValue"]);
        });
    }

    [Fact]
    public void BuildMetrics_ContainsAllExpectedMetrics()
    {
        var expectedMetrics = new[]
        {
            "Request Charge",
            "Retrieved document count",
            "Retrieved document size",
            "Output document count",
            "Output document size",
            "Index hit ratio",
            "Index lookup time",
            "Document load time",
            "Runtime execution time",
            "VMExecution execution time",
            "Query preparation time",
            "Document write time",
            "Total time",
        };

        var metrics = QueryCommand.BuildMetrics(0, null);
        var metricNames = metrics.Select(m => (string)m["metric"]).ToList();

        Assert.Equal(expectedMetrics.Length, metrics.Count);
        foreach (var expected in expectedMetrics)
        {
            Assert.Contains(expected, metricNames);
        }

        Assert.All(metrics, entry =>
        {
            Assert.Contains("metric", entry.Keys);
            Assert.Contains("value", entry.Keys);
            Assert.Contains("formattedValue", entry.Keys);
            Assert.Contains("tooltip", entry.Keys);
        });
    }

    [Fact]
    public void BuildMetrics_WithCumulative_ReturnsPopulatedValues()
    {
        var cumulative = new TestServerSideMetrics(
            retrievedDocumentCount: 100,
            retrievedDocumentSize: 2048,
            outputDocumentCount: 50,
            outputDocumentSize: 1024,
            indexHitRatio: 0.95,
            indexLookupTime: TimeSpan.FromMilliseconds(5),
            documentLoadTime: TimeSpan.FromMilliseconds(10),
            documentWriteTime: TimeSpan.FromMilliseconds(3),
            runtimeExecutionTime: TimeSpan.FromMilliseconds(20),
            vmExecutionTime: TimeSpan.FromMilliseconds(15),
            queryPreparationTime: TimeSpan.FromMilliseconds(2),
            totalTime: TimeSpan.FromMilliseconds(55));

        var metrics = QueryCommand.BuildMetrics(42.5, cumulative);
        Dictionary<string, object> M(string name) => metrics.First(m => (string)m["metric"] == name);

        Assert.Equal(13, metrics.Count);

        // Request Charge
        Assert.Equal(42.5, M("Request Charge")["value"]);
        Assert.Contains("42", (string)M("Request Charge")["formattedValue"]);
        Assert.EndsWith("RUs", (string)M("Request Charge")["formattedValue"]);

        // Document counts
        Assert.Equal(100L, M("Retrieved document count")["value"]);
        Assert.Equal("100", M("Retrieved document count")["formattedValue"]);

        Assert.Equal(2048L, M("Retrieved document size")["value"]);
        Assert.Equal("2048 bytes", M("Retrieved document size")["formattedValue"]);

        Assert.Equal(50L, M("Output document count")["value"]);
        Assert.Equal("50", M("Output document count")["formattedValue"]);

        Assert.Equal(1024L, M("Output document size")["value"]);
        Assert.Equal("1024 bytes", M("Output document size")["formattedValue"]);

        // Index hit ratio
        Assert.Equal(0.95, M("Index hit ratio")["value"]);
        Assert.Contains("95", (string)M("Index hit ratio")["formattedValue"]);

        // Times
        Assert.Equal(5.0, M("Index lookup time")["value"]);
        Assert.Equal("5 ms", M("Index lookup time")["formattedValue"]);

        Assert.Equal(10.0, M("Document load time")["value"]);
        Assert.Equal("10 ms", M("Document load time")["formattedValue"]);

        Assert.Equal(20.0, M("Runtime execution time")["value"]);
        Assert.Equal("20 ms", M("Runtime execution time")["formattedValue"]);

        Assert.Equal(15.0, M("VMExecution execution time")["value"]);
        Assert.Equal("15 ms", M("VMExecution execution time")["formattedValue"]);

        Assert.Equal(2.0, M("Query preparation time")["value"]);
        Assert.Equal("2 ms", M("Query preparation time")["formattedValue"]);

        Assert.Equal(3.0, M("Document write time")["value"]);
        Assert.Equal("3 ms", M("Document write time")["formattedValue"]);

        Assert.Equal(55.0, M("Total time")["value"]);
        Assert.Equal("55 ms", M("Total time")["formattedValue"]);
    }

    [Fact]
    public void BuildMetrics_CoversAllServerSideMetricsProperties()
    {
        // Maps every ServerSideMetrics property to the corresponding BuildMetrics metric name.
        // If the SDK adds a new property, this test fails until it's added here and in BuildMetrics.
        var propertyToMetric = new Dictionary<string, string>
        {
            { "RetrievedDocumentCount", "Retrieved document count" },
            { "RetrievedDocumentSize", "Retrieved document size" },
            { "OutputDocumentCount", "Output document count" },
            { "OutputDocumentSize", "Output document size" },
            { "IndexHitRatio", "Index hit ratio" },
            { "IndexLookupTime", "Index lookup time" },
            { "DocumentLoadTime", "Document load time" },
            { "DocumentWriteTime", "Document write time" },
            { "RuntimeExecutionTime", "Runtime execution time" },
            { "VMExecutionTime", "VMExecution execution time" },
            { "QueryPreparationTime", "Query preparation time" },
            { "TotalTime", "Total time" },
        };

        var sdkProperties = typeof(ServerSideMetrics)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        var metrics = QueryCommand.BuildMetrics(0, null);
        var metricNames = metrics.Select(m => (string)m["metric"]).ToHashSet();

        foreach (var prop in sdkProperties)
        {
            Assert.True(
                propertyToMetric.ContainsKey(prop),
                $"ServerSideMetrics.{prop} is not mapped in propertyToMetric. Add it to BuildMetrics and this test.");
            Assert.Contains(propertyToMetric[prop], metricNames);
        }
    }
}