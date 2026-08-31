// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Globalization;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Unit tests for <see cref="SchemaCommand"/>. Covers the pure helpers that clamp the
/// sample size and infer field types, which can be exercised without a live Cosmos DB
/// connection.
/// </summary>
public class SchemaCommandTests
{
    [Fact]
    public void SchemaCommand_IsRegistered()
    {
        var runner = new CommandRunner();

        Assert.True(runner.Commands.TryGetValue("schema", out var factory));
        Assert.Equal("schema", factory!.CommandName);
    }

    [Fact]
    public void NormalizeSample_UsesDefaultWhenMissing()
    {
        Assert.Equal(SchemaCommand.DefaultSample, SchemaCommand.NormalizeSample(null));
    }

    [Fact]
    public void NormalizeSample_ClampsBelowMinimum()
    {
        Assert.Equal(SchemaCommand.MinSample, SchemaCommand.NormalizeSample(0));
        Assert.Equal(SchemaCommand.MinSample, SchemaCommand.NormalizeSample(-5));
    }

    [Fact]
    public void NormalizeSample_ClampsAboveMaximum()
    {
        Assert.Equal(SchemaCommand.MaxSample, SchemaCommand.NormalizeSample(SchemaCommand.MaxSample + 1));
        Assert.Equal(SchemaCommand.MaxSample, SchemaCommand.NormalizeSample(int.MaxValue));
    }

    [Fact]
    public void NormalizeSample_KeepsValueInRange()
    {
        Assert.Equal(42, SchemaCommand.NormalizeSample(42));
    }

    [Fact]
    public void BuildSampleQueryText_UsesServerSideTopLimit()
    {
        Assert.Equal("SELECT TOP 42 * FROM c", SchemaCommand.BuildSampleQueryText(42));
    }

    [Fact]
    public void BuildSampleQueryText_FormatsLimitInvariantly()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");

            Assert.Equal("SELECT TOP 42 * FROM c", SchemaCommand.BuildSampleQueryText(42));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void InferSchema_ReportsTopLevelTypes()
    {
        var documents = Parse(
            "{\"id\":\"a\",\"price\":10,\"active\":true}",
            "{\"id\":\"b\",\"price\":20,\"active\":false}");

        var fields = SchemaCommand.InferSchema(documents);

        Assert.Equal(new[] { "active", "id", "price" }, fields.Select(f => f.Path));
        Assert.Equal(new[] { "string" }, Field(fields, "id").Types);
        Assert.Equal(new[] { "number" }, Field(fields, "price").Types);
        Assert.Equal(new[] { "boolean" }, Field(fields, "active").Types);
    }

    [Fact]
    public void InferSchema_TracksPresenceAcrossDocuments()
    {
        var documents = Parse(
            "{\"id\":\"a\",\"optional\":1}",
            "{\"id\":\"b\"}",
            "{\"id\":\"c\"}");

        var fields = SchemaCommand.InferSchema(documents);

        Assert.Equal(3, Field(fields, "id").Presence);
        Assert.Equal(1, Field(fields, "optional").Presence);
    }

    [Fact]
    public void InferSchema_CountsDuplicatePropertyOncePerDocument()
    {
        var documents = Parse("{\"value\":1,\"value\":\"text\"}");

        var field = Field(SchemaCommand.InferSchema(documents), "value");

        Assert.Equal(1, field.Presence);
        Assert.Equal(new[] { "number", "string" }, field.Types);
    }

    [Fact]
    public void InferSchema_RecordsMultipleTypesForSameField()
    {
        var documents = Parse(
            "{\"value\":1}",
            "{\"value\":\"text\"}",
            "{\"value\":null}");

        var types = Field(SchemaCommand.InferSchema(documents), "value").Types;

        Assert.Equal(new[] { "null", "number", "string" }, types);
    }

    [Fact]
    public void InferSchema_DescribesNestedObjectsWithDotNotation()
    {
        var documents = Parse("{\"address\":{\"city\":\"Seattle\",\"zip\":\"98101\"}}");

        var fields = SchemaCommand.InferSchema(documents);

        Assert.Equal(new[] { "object" }, Field(fields, "address").Types);
        Assert.Equal(new[] { "string" }, Field(fields, "address.city").Types);
        Assert.Equal(new[] { "string" }, Field(fields, "address.zip").Types);
    }

    [Fact]
    public void InferSchema_HonorsMaxDepth()
    {
        var documents = Parse("{\"a\":{\"b\":{\"c\":1}}}");

        var fields = SchemaCommand.InferSchema(documents, maxDepth: 1);

        Assert.Contains(fields, f => f.Path == "a");
        Assert.DoesNotContain(fields, f => f.Path == "a.b");
    }

    [Fact]
    public void InferSchema_ReportsArrayType()
    {
        var documents = Parse("{\"tags\":[1,2,3]}");

        Assert.Equal(new[] { "array" }, Field(SchemaCommand.InferSchema(documents), "tags").Types);
    }

    [Fact]
    public void InferSchema_IgnoresNonObjectDocuments()
    {
        var documents = Parse("42", "\"text\"", "{\"id\":\"a\"}");

        var fields = SchemaCommand.InferSchema(documents);

        Assert.Single(fields);
        Assert.Equal("id", fields[0].Path);
    }

    [Fact]
    public void BuildResult_ReturnsStructuredSchemaSummary()
    {
        var properties = new ContainerProperties("Products", "/category");
        var fields = new[] { new SchemaCommand.FieldSchema("id", new[] { "string" }, 2) };

        var state = SchemaCommand.BuildResult("MyDB", "Products", properties, 12, 20, 2, fields);
        var result = Assert.IsType<ShellJson>(state.Result).Value;

        Assert.Equal("MyDB", result.GetProperty("database").GetString());
        Assert.Equal("Products", result.GetProperty("container").GetString());
        Assert.Equal("/category", result.GetProperty("partitionKeyPaths")[0].GetString());
        Assert.Equal(12, result.GetProperty("documentCountEstimate").GetInt64());
        Assert.Equal(20, result.GetProperty("sampleSize").GetInt32());
        Assert.Equal(2, result.GetProperty("sampledDocuments").GetInt32());
        Assert.True(result.TryGetProperty("indexingPolicy", out _));
        Assert.Equal("id", result.GetProperty("fields")[0].GetProperty("path").GetString());
    }

    [Fact]
    public void BuildFieldsOnlyResult_ReturnsOnlySampleCountAndFields()
    {
        var fields = new[] { new SchemaCommand.FieldSchema("id", new[] { "string" }, 2) };

        var state = SchemaCommand.BuildFieldsOnlyResult(2, fields);
        var result = Assert.IsType<ShellJson>(state.Result).Value;

        Assert.Equal(2, result.EnumerateObject().Count());
        Assert.Equal(2, result.GetProperty("sampledDocuments").GetInt32());
        Assert.Equal("id", result.GetProperty("fields")[0].GetProperty("path").GetString());
        Assert.Equal(new[] { "string" }, result.GetProperty("fields")[0].GetProperty("types").EnumerateArray().Select(type => type.GetString()));
        Assert.Equal(2, result.GetProperty("fields")[0].GetProperty("presence").GetInt32());
    }

    private static SchemaCommand.FieldSchema Field(IReadOnlyList<SchemaCommand.FieldSchema> fields, string path)
    {
        return fields.Single(f => f.Path == path);
    }

    private static List<JsonElement> Parse(params string[] json)
    {
        return json.Select(text =>
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }).ToList();
    }
}
