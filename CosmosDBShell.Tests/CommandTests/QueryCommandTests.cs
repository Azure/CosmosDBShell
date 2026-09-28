// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Microsoft.Azure.Cosmos;
using NSubstitute;
using Spectre.Console;

[Collection(CosmosShell.Tests.Shell.ThemeStateTestCollection.Name)]
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
    public void CreateCommandState_WithoutCommandFormat_DefersToSessionDefault()
    {
        var state = QueryCommand.CreateCommandState(null);

        Assert.False(state.OutputFormatExplicitlySet);
    }

    [Fact]
    public void CreateCommandState_WithCommandFormat_MarksFormatExplicit()
    {
        var state = QueryCommand.CreateCommandState("json");

        Assert.True(state.OutputFormatExplicitlySet);
        Assert.Equal(OutputFormat.JSon, state.OutputFormat);
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
    public void PageExceedsLimit_ReturnsTrueWhenCurrentPageWouldBeTruncated()
    {
        var page = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "3" },
            new { id = "4" },
        });

        Assert.True(QueryCommand.PageExceedsLimit(currentCount: 2, page, maxItemCount: 3));
    }

    [Fact]
    public void PageExceedsLimit_ReturnsFalseWhenCurrentPageFitsRemainingCapacity()
    {
        var page = JsonSerializer.SerializeToElement(new[]
        {
            new { id = "3" },
        });

        Assert.False(QueryCommand.PageExceedsLimit(currentCount: 2, page, maxItemCount: 3));
        Assert.False(QueryCommand.PageExceedsLimit(currentCount: 2, page, maxItemCount: null));
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

    [Fact]
    public void EvaluatePlan_NoUtilizedIndexes_ReportsFullScan()
    {
        var evaluation = QueryCommand.EvaluatePlan(
            planAvailable: true,
            utilizedIndexes: [],
            potentialIndexes: [],
            indexHitRatio: 0,
            retrievedDocumentCount: 1000,
            outputDocumentCount: 1);

        Assert.True(evaluation.FullScan);
        Assert.False(evaluation.IndexSeek);
        Assert.True(evaluation.PlanAvailable);
        Assert.Empty(evaluation.UtilizedIndexes);
    }

    [Fact]
    public void EvaluatePlan_UnavailablePlan_DoesNotReportScanType()
    {
        var evaluation = QueryCommand.EvaluatePlan(
            planAvailable: false,
            utilizedIndexes: [],
            potentialIndexes: [],
            indexHitRatio: null,
            retrievedDocumentCount: null,
            outputDocumentCount: null);

        Assert.False(evaluation.PlanAvailable);
        Assert.False(evaluation.FullScan);
        Assert.False(evaluation.IndexSeek);
    }

    [Fact]
    public void EvaluatePlan_WithUtilizedIndexes_ReportsIndexSeek()
    {
        var evaluation = QueryCommand.EvaluatePlan(
            planAvailable: true,
            utilizedIndexes: ["/city/?"],
            potentialIndexes: [],
            indexHitRatio: 1,
            retrievedDocumentCount: 1,
            outputDocumentCount: 1);

        Assert.False(evaluation.FullScan);
        Assert.True(evaluation.IndexSeek);
        Assert.Equal(1, evaluation.IndexHitRatio);
        Assert.Collection(evaluation.UtilizedIndexes, spec => Assert.Equal("/city/?", spec));
    }

    [Fact]
    public void EvaluatePlan_PreservesPotentialIndexRecommendations()
    {
        var evaluation = QueryCommand.EvaluatePlan(
            planAvailable: true,
            utilizedIndexes: ["/city/?"],
            potentialIndexes: ["/age/?"],
            indexHitRatio: 0.5,
            retrievedDocumentCount: 200,
            outputDocumentCount: 100);

        Assert.True(evaluation.IndexSeek);
        Assert.Collection(evaluation.PotentialIndexes, spec => Assert.Equal("/age/?", spec));
        Assert.Equal(200, evaluation.RetrievedDocumentCount);
        Assert.Equal(100, evaluation.OutputDocumentCount);
    }

    [Fact]
    public void BuildPlanMessages_FormatsIndexHitRatioInvariantly()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var evaluation = QueryCommand.EvaluatePlan(
                planAvailable: true,
                utilizedIndexes: ["/city/?"],
                potentialIndexes: [],
                indexHitRatio: 0.5,
                retrievedDocumentCount: 1,
                outputDocumentCount: 1);

            var messages = QueryCommand.BuildPlanMessages(evaluation);

            Assert.Contains(messages, message => message.Contains("0.5", StringComparison.Ordinal));
            Assert.DoesNotContain(messages, message => message.Contains("0,5", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void ParseIndexPlan_ExtractsSingleAndCompositeIndexSpecs()
    {
        const string indexMetrics = """
        {
            "UtilizedIndexes": {
                "SingleIndexes": [ { "IndexSpec": "/city/?" } ],
                "CompositeIndexes": [ { "IndexSpecs": [ "/age ASC", "/name ASC" ] } ]
            },
            "PotentialIndexes": {
                "SingleIndexes": [ { "IndexSpec": "/status/?" } ],
                "CompositeIndexes": []
            }
        }
        """;

        var (available, utilized, potential) = QueryCommand.ParseIndexPlan(indexMetrics);

        Assert.True(available);
        Assert.Equal(["/city/?", "/age ASC, /name ASC"], utilized);
        Assert.Equal(["/status/?"], potential);
    }

    [Fact]
    public void ParseIndexPlan_ExtractsDirectIndexArrays()
    {
        const string indexMetrics = """
        {
            "UtilizedIndexes": [ { "IndexSpec": "/city/?" } ],
            "PotentialIndexes": [ { "IndexSpec": "/status/?" } ]
        }
        """;

        var (available, utilized, potential) = QueryCommand.ParseIndexPlan(indexMetrics);

        Assert.True(available);
        Assert.Equal(["/city/?"], utilized);
        Assert.Equal(["/status/?"], potential);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseIndexPlan_NullOrEmpty_ReturnsEmptyLists(string? indexMetrics)
    {
        var (available, utilized, potential) = QueryCommand.ParseIndexPlan(indexMetrics);

        Assert.False(available);
        Assert.Empty(utilized);
        Assert.Empty(potential);
    }

    [Fact]
    public void ParseIndexPlan_MalformedJson_ReturnsEmptyLists()
    {
        var (available, utilized, potential) = QueryCommand.ParseIndexPlan("{ not valid json");

        Assert.False(available);
        Assert.Empty(utilized);
        Assert.Empty(potential);
    }

    [Fact]
    public void ParseIndexPlan_UnexpectedObject_ReturnsUnavailable()
    {
        var (available, utilized, potential) = QueryCommand.ParseIndexPlan("{}");

        Assert.False(available);
        Assert.Empty(utilized);
        Assert.Empty(potential);
    }

    [Fact]
    public void ParseIndexPlan_UnrecognizedIndexGroup_ReturnsUnavailable()
    {
        const string indexMetrics = "{\"UtilizedIndexes\":{\"UnexpectedIndexes\":[]}}";

        var (available, utilized, potential) = QueryCommand.ParseIndexPlan(indexMetrics);

        Assert.False(available);
        Assert.Empty(utilized);
        Assert.Empty(potential);
    }

    [Fact]
    public void ParseIndexPlan_TrimsSpecsAndIgnoresWhitespaceOnlyValues()
    {
        const string indexMetrics = """
        {
            "UtilizedIndexes": [
                "   ",
                " /city/? ",
                { "IndexSpec": " /name/? " },
                { "IndexSpecs": [ " /age ASC ", "   ", "/name ASC " ] }
            ]
        }
        """;

        var (available, utilized, potential) = QueryCommand.ParseIndexPlan(indexMetrics);

        Assert.True(available);
        Assert.Equal(["/city/?", "/name/?", "/age ASC, /name ASC"], utilized);
        Assert.Empty(potential);
    }

    [Fact]
    public void ParseIndexPlan_RecognizedEmptyGroups_ReturnsAvailable()
    {
        const string indexMetrics = "{\"UtilizedIndexes\":{},\"PotentialIndexes\":{}}";

        var (available, utilized, potential) = QueryCommand.ParseIndexPlan(indexMetrics);

        Assert.True(available);
        Assert.Empty(utilized);
        Assert.Empty(potential);
    }

    [Fact]
    public void TryReadContinuationToken_ResponseWithoutToken_ReportsSupported()
    {
        using var response = new ResponseMessage(HttpStatusCode.OK);

        Assert.True(QueryCommand.TryReadContinuationToken(response, out var continuationToken));
        Assert.Null(continuationToken);
    }

    [Fact]
    public void TryReadContinuationToken_ResponseWithToken_ReturnsToken()
    {
        using var response = new PageResponse("{\"_count\":0,\"Documents\":[]}", () => "next-page");

        Assert.True(QueryCommand.TryReadContinuationToken(response, out var continuationToken));
        Assert.Equal("next-page", continuationToken);
    }

    [Theory]
    [InlineData("Continuation tokens are not supported for the non streaming order by pipeline.")]
    [InlineData("Continuation tokens are not supported by hybrid search.")]
    [InlineData("DISTINCT queries only return continuation tokens when there is a matching ORDER BY clause.")]
    public void TryReadContinuationToken_PipelineWithoutTokenSupport_ReportsUnsupported(string message)
    {
        using var response = new PageResponse("{\"_count\":0,\"Documents\":[]}", () => throw new ArgumentException(message));

        Assert.False(QueryCommand.TryReadContinuationToken(response, out var continuationToken));
        Assert.Null(continuationToken);
    }

    [Fact]
    public async Task ExecuteQueryAsync_PipelineWithoutTokenSupport_ReturnsDocuments()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var iterator = new FakeFeedIterator(
            NonResumablePage("Continuation tokens are not supported for the non streaming order by pipeline.", 1.5, "1", "2", "6"));
        var container = CreateContainer(iterator);
        var command = new QueryCommand { Query = "SELECT TOP 3 c.id FROM c ORDER BY VectorDistance(c.embedding, [1,0,0])", Max = 10 };

        var result = await command.ExecuteQueryAsync(container, shell, CancellationToken.None);

        Assert.Equal(["1", "2", "6"], ReadIds(result));
        Assert.Null(result.ContinuationToken);
        Assert.False(result.IncompleteWithoutContinuation);
        Assert.Equal(1.5, result.RequestCharge);
    }

    [Fact]
    public async Task ExecuteQueryAsync_PipelineWithoutTokenSupport_AdvancesThroughEmptyPages()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var iterator = new FakeFeedIterator(
            NonResumablePage("Continuation tokens are not supported by hybrid search.", 1, "1"),
            NonResumablePage("Continuation tokens are not supported by hybrid search.", 2),
            NonResumablePage("Continuation tokens are not supported by hybrid search.", 3, "2", "6"));
        var container = CreateContainer(iterator);
        var command = new QueryCommand { Query = "SELECT TOP 3 c.id FROM c ORDER BY RANK FullTextScore(c.text, \"cosmos\")", Max = 10, IsMcpRequest = true };

        var result = await command.ExecuteQueryAsync(container, shell, CancellationToken.None);

        Assert.Equal(["1", "2", "6"], ReadIds(result));
        Assert.Equal(3, iterator.ReadCount);
        Assert.Null(result.ContinuationToken);
        Assert.False(result.IncompleteWithoutContinuation);
        Assert.Equal(6, result.RequestCharge);
    }

    [Fact]
    public async Task ExecuteQueryAsync_PipelineWithoutTokenSupport_ReportsIncompleteResultAtLimit()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var iterator = new FakeFeedIterator(
            NonResumablePage("Continuation tokens are not supported by hybrid search.", 1, "1", "2"),
            NonResumablePage("Continuation tokens are not supported by hybrid search.", 1, "6"));
        var container = CreateContainer(iterator);
        var command = new QueryCommand { Query = "SELECT c.id FROM c ORDER BY RANK FullTextScore(c.text, \"cosmos\")", Max = 2, IsMcpRequest = true };

        var output = await CaptureConsoleAsync(() => command.ExecuteQueryAsync(container, shell, CancellationToken.None));

        Assert.Equal(["1", "2"], ReadIds(output.Result));
        Assert.Null(output.Result.ContinuationToken);
        Assert.True(output.Result.IncompleteWithoutContinuation);
        Assert.Contains("cannot be resumed", output.Text);
    }

    [Fact]
    public async Task ExecuteQueryAsync_PipelineWithoutTokenSupport_CancelledBetweenPages_ReportsIncompleteResult()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var cancellation = new CancellationTokenSource();
        using var iterator = new FakeFeedIterator(
            NonResumablePage("Continuation tokens are not supported by hybrid search.", 1, "1"),
            NonResumablePage("Continuation tokens are not supported by hybrid search.", 1, "2"));
        iterator.AfterRead = cancellation.Cancel;
        var container = CreateContainer(iterator);
        var command = new QueryCommand { Query = "SELECT c.id FROM c ORDER BY RANK FullTextScore(c.text, \"cosmos\")", Max = 10, IsMcpRequest = true };

        var result = await command.ExecuteQueryAsync(container, shell, cancellation.Token);

        Assert.Equal(["1"], ReadIds(result));
        Assert.Equal(1, iterator.ReadCount);
        Assert.Null(result.ContinuationToken);
        Assert.True(result.IncompleteWithoutContinuation);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ObjectShapedDistinctWithOrderBy_ReturnsDocumentsWithoutToken()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var iterator = new FakeFeedIterator(
            NonResumableDocumentPage(
                "DISTINCT queries only return continuation tokens when there is a matching ORDER BY clause.",
                1,
                "{\"category\":\"A\"}",
                "{\"category\":\"B\"}",
                "{\"category\":\"C\"}"));
        var container = CreateContainer(iterator);
        var command = new QueryCommand { Query = "SELECT DISTINCT c.category FROM c ORDER BY c.category", Max = 10, IsMcpRequest = true };

        var result = await command.ExecuteQueryAsync(container, shell, CancellationToken.None);

        Assert.Equal(["A", "B", "C"], ReadValues(result, "category"));
        Assert.Null(result.ContinuationToken);
        Assert.False(result.IncompleteWithoutContinuation);
    }

    [Fact]
    public async Task ExecuteQueryAsync_TokenExportRefusedOnLaterPage_DiscardsEarlierToken()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var iterator = new FakeFeedIterator(
            ResumablePage("stale-token", 1, "1"),
            NonResumablePage("Continuation tokens are not supported by hybrid search.", 1, "2"));
        var container = CreateContainer(iterator);
        var command = new QueryCommand { Query = "SELECT c.id FROM c", Max = 10 };

        var result = await command.ExecuteQueryAsync(container, shell, CancellationToken.None);

        Assert.Equal(["1", "2"], ReadIds(result));
        Assert.Equal(2, iterator.ReadCount);
        Assert.Null(result.ContinuationToken);
        Assert.False(result.IncompleteWithoutContinuation);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ResumablePage_KeepsSingleMcpPageAndToken()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var iterator = new FakeFeedIterator(
            ResumablePage("next-page", 1, "A", "B"),
            ResumablePage(null, 1, "C"));
        var container = CreateContainer(iterator);
        var command = new QueryCommand { Query = "SELECT DISTINCT VALUE c.category FROM c ORDER BY c.category", Max = 10, IsMcpRequest = true };

        var result = await command.ExecuteQueryAsync(container, shell, CancellationToken.None);

        Assert.Equal(1, iterator.ReadCount);
        Assert.Equal("next-page", result.ContinuationToken);
        Assert.False(result.IncompleteWithoutContinuation);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ResumableQueryAtLimit_ReportsLimitWithoutResumeWarning()
    {
        using var shell = ShellInterpreter.CreateInstance();
        using var iterator = new FakeFeedIterator(
            ResumablePage("next-page", 1, "1", "2", "3"));
        var container = CreateContainer(iterator);
        var command = new QueryCommand { Query = "SELECT * FROM c", Max = 2 };

        var output = await CaptureConsoleAsync(() => command.ExecuteQueryAsync(container, shell, CancellationToken.None));

        Assert.Equal(["1", "2"], ReadIds(output.Result));
        Assert.Equal("next-page", output.Result.ContinuationToken);
        Assert.False(output.Result.IncompleteWithoutContinuation);
        Assert.Contains("Results limited to 2 items", output.Text);
        Assert.DoesNotContain("cannot be resumed", output.Text);
    }

    private static Container CreateContainer(FeedIterator iterator)
    {
        var container = Substitute.For<Container>();
        container.GetItemQueryStreamIterator(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>()).Returns(iterator);
        return container;
    }

    private static ResponseMessage NonResumablePage(string message, double requestCharge, params string[] ids)
    {
        return NonResumableDocumentPage(message, requestCharge, [.. ids.Select(id => $"{{\"id\":\"{id}\"}}")]);
    }

    private static ResponseMessage NonResumableDocumentPage(string message, double requestCharge, params string[] documents)
    {
        return CreatePage(requestCharge, () => throw new ArgumentException(message), documents);
    }

    private static ResponseMessage ResumablePage(string? continuationToken, double requestCharge, params string[] ids)
    {
        return CreatePage(requestCharge, () => continuationToken, [.. ids.Select(id => $"{{\"id\":\"{id}\"}}")]);
    }

    private static ResponseMessage CreatePage(double requestCharge, Func<string?> continuationToken, string[] documents)
    {
        var response = new PageResponse($"{{\"_count\":{documents.Length},\"Documents\":[{string.Join(",", documents)}]}}", continuationToken);
        response.Headers.Add("x-ms-request-charge", requestCharge.ToString(CultureInfo.InvariantCulture));
        return response;
    }

    private static string[] ReadIds(CommandState state)
    {
        return ReadValues(state, "id");
    }

    private static string[] ReadValues(CommandState state, string property)
    {
        using var document = JsonDocument.Parse(state.GenerateOutputText());
        return [.. document.RootElement.GetProperty("values").EnumerateArray().Select(value => value.GetProperty(property).GetString()!)];
    }

    private static async Task<(CommandState Result, string Text)> CaptureConsoleAsync(Func<Task<CommandState>> action)
    {
        var saved = AnsiConsole.Console;
        using var writer = new StringWriter();
        try
        {
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(writer),
            });
            AnsiConsole.Console.Profile.Width = 200;

            var result = await action();
            return (result, writer.ToString());
        }
        finally
        {
            AnsiConsole.Console = saved;
        }
    }

    private sealed class PageResponse : ResponseMessage
    {
        private readonly Func<string?> continuationToken;

        public PageResponse(string content, Func<string?> continuationToken)
            : base(HttpStatusCode.OK)
        {
            this.continuationToken = continuationToken;
            this.Content = new MemoryStream(Encoding.UTF8.GetBytes(content));
        }

        public override string ContinuationToken => this.continuationToken()!;
    }

    private sealed class FakeFeedIterator : FeedIterator
    {
        private readonly Queue<ResponseMessage> pages;

        public FakeFeedIterator(params ResponseMessage[] pages)
        {
            this.pages = new Queue<ResponseMessage>(pages);
        }

        public int ReadCount { get; private set; }

        public Action? AfterRead { get; set; }

        public override bool HasMoreResults => this.pages.Count > 0;

        public override Task<ResponseMessage> ReadNextAsync(CancellationToken cancellationToken = default)
        {
            this.ReadCount++;
            var page = this.pages.Dequeue();
            this.AfterRead?.Invoke();
            return Task.FromResult(page);
        }
    }
}