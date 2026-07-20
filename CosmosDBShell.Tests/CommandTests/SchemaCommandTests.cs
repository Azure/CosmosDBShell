// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;

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

    private static SchemaCommand.FieldSchema Field(IReadOnlyList<SchemaCommand.FieldSchema> fields, string path)
    {
        return fields.Single(f => f.Path == path);
    }

    private static List<JsonElement> Parse(params string[] json)
    {
        var documents = new List<JsonElement>(json.Length);
        foreach (var text in json)
        {
            using var document = JsonDocument.Parse(text);
            documents.Add(document.RootElement.Clone());
        }

        return documents;
    }
}
