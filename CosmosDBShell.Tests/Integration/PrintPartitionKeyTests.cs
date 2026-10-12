// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Cosmos.Shell.States;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

public class PrintPartitionKeyTests : EmulatorTestBase
{
    [Fact]
    public async Task Print_MixedKeyFixtures_SelectsExactDocumentsThroughShellPointReads()
    {
        var token = TestContext.Current.CancellationToken;
        var connectionString = ParsedDocDBConnectionString.BuildEmulatorConnectionString(EmulatorEndpoint);
        using var fixtureClient = new CosmosClient(connectionString, CreateClientOptions());
        var database = (await fixtureClient.CreateDatabaseAsync($"PrintKeys_{Guid.NewGuid():N}", cancellationToken: token)).Database;
        try
        {
            await database.CreateContainerAsync(new ContainerProperties("Scalar", "/pk"), cancellationToken: token);
            await database.CreateContainerAsync(new ContainerProperties("Two", new[] { "/first", "/second" }), cancellationToken: token);
            await database.CreateContainerAsync(new ContainerProperties("Three", new[] { "/first", "/second", "/third" }), cancellationToken: token);

            var recorder = new RequestRecorder();
            var shellOptions = CreateClientOptions();
            shellOptions.CustomHandlers.Add(recorder);
            Shell.State = new ConnectedState(new CosmosClient(connectionString, shellOptions));

            var fixtures = CreateFixtures();
            foreach (var fixture in fixtures)
            {
                var container = database.GetContainer(fixture.ContainerName);
                using var body = new MemoryStream(Encoding.UTF8.GetBytes(fixture.Document));
                using var created = await container.CreateItemStreamAsync(body, fixture.PartitionKey, cancellationToken: token);
                Assert.True(created.StatusCode == HttpStatusCode.Created, $"{fixture.Document}: {created.StatusCode}");
            }

            foreach (var fixture in fixtures)
            {
                using var expected = JsonDocument.Parse(fixture.Document);
                var id = expected.RootElement.GetProperty("id").GetString()!;
                using var sdkResponse = await database.GetContainer(fixture.ContainerName).ReadItemStreamAsync(id, fixture.PartitionKey, cancellationToken: token);
                Assert.Equal(HttpStatusCode.OK, sdkResponse.StatusCode);
                using var sdkDocument = await JsonDocument.ParseAsync(sdkResponse.Content, cancellationToken: token);
                AssertDocumentEquals(fixture.Document, sdkDocument.RootElement);

                foreach (var arguments in fixture.Arguments)
                {
                    var command = $"print {id} {arguments} --database={database.Id} --container={fixture.ContainerName}";
                    var actual = await this.ReadThroughShellAsync(command, recorder, fixture.PartitionKey);
                    AssertDocumentEquals(fixture.Document, actual);
                }
            }

            var navigation = await RunScriptAsync($"cd {database.Id}/Three");
            Assert.False(navigation.IsError, FormatError(navigation));
            var scopedFixture = fixtures.First(fixture => fixture.ContainerName == "Three");
            var scopedResult = await this.ReadThroughShellAsync("print order-42-three-numeric --pk='[\"tenant-1\",680,true]'", recorder, scopedFixture.PartitionKey);
            AssertDocumentEquals(fixtures.First(fixture => fixture.ContainerName == "Three").Document, scopedResult);

            await this.AssertInputErrorsAsync(database.Id, recorder);

            recorder.Requests.Clear();
            var missing = await RunScriptAsync($"print missing --pk=680 --database={database.Id} --container=Scalar");
            Assert.True(missing.IsError);
            Assert.Contains("not found using partition key", GetErrorMessage(missing));
            AssertPointRead(recorder, new PartitionKey(680d));
        }
        finally
        {
            await database.DeleteAsync(cancellationToken: CancellationToken.None);
        }
    }

    private static CosmosClientOptions CreateClientOptions() => new()
    {
        ConnectionMode = ConnectionMode.Gateway,
        ServerCertificateCustomValidationCallback = (_, _, _) => true,
    };

    private static KeyFixture[] CreateFixtures() =>
    [
        new("Scalar", """{"id":"product-680-numeric","pk":680,"marker":"numeric"}""", new PartitionKey(680d),
            "--pk=680", "--partition-key 680", "--pk=\"680\""),
        new("Scalar", """{"id":"product-680-numeric-string","pk":"680","marker":"numeric-string"}""", new PartitionKey("680"),
            "680", "\"680\"", "--partition-key='\"680\"'"),
        new("Scalar", """{"id":"product-680-fraction","pk":680.5,"marker":"fraction"}""", new PartitionKey(680.5d), "--pk=680.5"),
        new("Scalar", """{"id":"product-680-boolean","pk":true,"marker":"boolean"}""", new PartitionKey(true), "--pk=true"),
        new("Scalar", """{"id":"product-680-false","pk":false,"marker":"false"}""", new PartitionKey(false), "--pk=false"),
        new("Scalar", """{"id":"product-680-boolean-string","pk":"true","marker":"boolean-string"}""", new PartitionKey("true"),
            "true", "--pk='\"true\"'"),
        new("Scalar", """{"id":"product-680-null","pk":null,"marker":"null"}""", PartitionKey.Null, "--pk=null"),
        new("Scalar", """{"id":"product-680-null-string","pk":"null","marker":"null-string"}""", new PartitionKey("null"),
            "'null'", "--pk='\"null\"'"),
        new("Scalar", """{"id":"product-680-ordinary","pk":"customer-1","marker":"ordinary"}""", new PartitionKey("customer-1"),
            "customer-1", "--pk=customer-1"),
        new("Scalar", """{"id":"product-680-empty","pk":"","marker":"empty"}""", new PartitionKey(""), "''", "--pk='\"\"'"),
        new("Scalar", """{"id":"product-680-whitespace","pk":" ","marker":"whitespace"}""", new PartitionKey(" "), "' '", "--pk='\" \"'"),
        new("Scalar", """{"id":"product-680-array-string","pk":"[1001,\"customer-1001\"]","marker":"array-string"}""",
            new PartitionKey("[1001,\"customer-1001\"]"), "'[1001,\"customer-1001\"]'"),
        new("Two", """{"id":"customer-1001-two-numeric","first":1001,"second":"customer-1001","marker":"two-numeric"}""",
            new PartitionKeyBuilder().Add(1001d).Add("customer-1001").Build(), "--pk='[1001,\"customer-1001\"]'"),
        new("Two", """{"id":"customer-1001-two-string","first":"1001","second":"customer-1001","marker":"two-string"}""",
            new PartitionKeyBuilder().Add("1001").Add("customer-1001").Build(), "--pk='[\"1001\",\"customer-1001\"]'"),
        new("Two", """{"id":"customer-1001-two-reversed","first":"customer-1001","second":1001,"marker":"two-reversed"}""",
            new PartitionKeyBuilder().Add("customer-1001").Add(1001d).Build(), "--pk='[\"customer-1001\",1001]'"),
        new("Two", """{"id":"customer-1001-two-boolean","first":"tenant-1","second":true,"marker":"two-boolean"}""",
            new PartitionKeyBuilder().Add("tenant-1").Add(true).Build(), "--pk='[\"tenant-1\",true]'"),
        new("Three", """{"id":"order-42-three-numeric","first":"tenant-1","second":680,"third":true,"marker":"three-numeric"}""",
            new PartitionKeyBuilder().Add("tenant-1").Add(680d).Add(true).Build(), "--pk='[\"tenant-1\",680,true]'"),
        new("Three", """{"id":"order-42-three-string","first":"tenant-1","second":"680","third":true,"marker":"three-string"}""",
            new PartitionKeyBuilder().Add("tenant-1").Add("680").Add(true).Build(), "--pk='[\"tenant-1\",\"680\",true]'"),
        new("Three", """{"id":"order-42-three-reversed","first":680,"second":"tenant-1","third":true,"marker":"three-reversed"}""",
            new PartitionKeyBuilder().Add(680d).Add("tenant-1").Add(true).Build(), "--pk='[680,\"tenant-1\",true]'"),
    ];

    private static void AssertDocumentEquals(string expected, JsonElement actual)
    {
        var actualDocument = JsonNode.Parse(actual.GetRawText())!.AsObject();
        foreach (var metadata in new[] { "_rid", "_self", "_etag", "_attachments", "_ts" })
        {
            actualDocument.Remove(metadata);
        }

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actualDocument), $"Expected {expected}, received {actualDocument}");
    }

    private static void AssertPointRead(RequestRecorder recorder, PartitionKey expectedKey)
    {
        Assert.All(recorder.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        var read = Assert.Single(recorder.Requests, request => request.Uri.Contains("/docs/", StringComparison.Ordinal));
        using var expected = JsonDocument.Parse(expectedKey.ToString());
        using var actual = JsonDocument.Parse(Assert.IsType<string>(read.PartitionKey));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
    }

    private async Task<JsonElement> ReadThroughShellAsync(string command, RequestRecorder recorder, PartitionKey expectedKey)
    {
        recorder.Requests.Clear();
        var output = CaptureOutputFile();
        try
        {
            var state = await RunScriptAsync(command);
            Assert.False(state.IsError, FormatError(state));
            AssertPointRead(recorder, expectedKey);
            using var document = JsonDocument.Parse(await ReadRedirectAsync(output));
            return document.RootElement.Clone();
        }
        finally
        {
            Shell.StdOutRedirect = null;
        }
    }

    private async Task AssertInputErrorsAsync(string databaseName, RequestRecorder recorder)
    {
        (string Arguments, string ExpectedMessage)[] invalid =
        [
            ("customer-1001", "positional string key or --partition-key"),
            ("--pk=680", "ID of the item"),
            ("customer-1001 --pk", "requires a value"),
            ("customer-1001 legacy --pk=680", "not both"),
            ("customer-1001 '' --pk=680", "not both"),
            ("customer-1001 --pk='[1001,]'", "Invalid partition key JSON"),
            ("customer-1001 --pk='{}'", "Invalid partition key JSON"),
            ("customer-1001 --pk='[{},1]'", "Invalid partition key JSON"),
            ("customer-1001 --pk='[[1],2]'", "Invalid partition key JSON"),
            ("customer-1001 --pk='[]'", "Invalid partition key JSON"),
            ("customer-1001 --pk='[1,2,3,4]'", "Invalid partition key JSON"),
            ("customer-1001 --pk='1e400'", "Invalid partition key JSON"),
            ("customer-1001 --pk=1001", "all 2 components"),
            ("customer-1001 --pk='[1001]'", "all 2 components"),
            ("customer-1001 --pk='[1001,\"customer-1001\",true]'", "all 2 components"),
        ];

        foreach (var (arguments, expectedMessage) in invalid)
        {
            recorder.Requests.Clear();
            var state = await RunScriptAsync($"print {arguments} --database={databaseName} --container=Two");
            Assert.True(state.IsError, arguments);
            var error = GetErrorMessage(state);
            Assert.True(error.Contains(expectedMessage, StringComparison.Ordinal), $"{arguments}: {error}");
            Assert.DoesNotContain(recorder.Requests, request => request.Uri.Contains("/docs", StringComparison.Ordinal));
        }
    }

    private sealed record KeyFixture(string ContainerName, string Document, PartitionKey PartitionKey, params string[] Arguments);

    private sealed class RequestRecorder : RequestHandler
    {
        public List<(HttpMethod Method, string Uri, string? PartitionKey)> Requests { get; } = [];

        public override Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
        {
            this.Requests.Add((request.Method, request.RequestUri.OriginalString, request.Headers["x-ms-documentdb-partitionkey"]));
            return base.SendAsync(request, cancellationToken);
        }
    }
}
