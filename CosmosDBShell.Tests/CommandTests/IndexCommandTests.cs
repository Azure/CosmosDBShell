// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Unit tests for <see cref="IndexCommand"/>. Covers the pure helpers that mutate
/// the indexing policy JSON, which can be exercised without a live Cosmos DB connection.
/// </summary>
public class IndexCommandTests
{
    private const string SamplePolicy =
        "{\"indexingMode\":\"consistent\",\"automatic\":true," +
        "\"includedPaths\":[{\"path\":\"/*\"}]," +
        "\"excludedPaths\":[{\"path\":\"/\\\"_etag\\\"/?\"}]}";

    [Fact]
    public void AddIncludedPaths_AddsNewPath()
    {
        var result = IndexCommand.AddIncludedPaths(SamplePolicy, ["/address/*"]);

        var included = IncludedPaths(result);
        Assert.Contains("/*", included);
        Assert.Contains("/address/*", included);
    }

    [Fact]
    public void AddIncludedPaths_DoesNotDuplicateExistingPath()
    {
        var result = IndexCommand.AddIncludedPaths(SamplePolicy, ["/*"]);

        var included = IncludedPaths(result);
        Assert.Single(included, "/*");
    }

    [Fact]
    public void AddIncludedPaths_RemovesMatchingExcludedPath()
    {
        const string policy = "{\"includedPaths\":[],\"excludedPaths\":[{\"path\":\"/address/*\"}]}";

        var result = IndexCommand.AddIncludedPaths(policy, ["/address/*"]);

        Assert.Contains("/address/*", IncludedPaths(result));
        Assert.DoesNotContain("/address/*", ExcludedPaths(result));
    }

    [Fact]
    public void AddIncludedPaths_CreatesIncludedPathsWhenMissing()
    {
        const string policy = "{\"indexingMode\":\"consistent\"}";

        var result = IndexCommand.AddIncludedPaths(policy, ["/address/*"]);

        Assert.Contains("/address/*", IncludedPaths(result));
    }

    [Fact]
    public void RemovePaths_RemovesFromIncludedAndExcluded()
    {
        const string policy =
            "{\"includedPaths\":[{\"path\":\"/a/*\"},{\"path\":\"/b/*\"}]," +
            "\"excludedPaths\":[{\"path\":\"/a/*\"}]}";

        var result = IndexCommand.RemovePaths(policy, ["/a/*"]);

        Assert.DoesNotContain("/a/*", IncludedPaths(result));
        Assert.DoesNotContain("/a/*", ExcludedPaths(result));
        Assert.Contains("/b/*", IncludedPaths(result));
    }

    [Fact]
    public void RemovePaths_NoOpWhenPathAbsent()
    {
        var result = IndexCommand.RemovePaths(SamplePolicy, ["/missing/*"]);

        Assert.Contains("/*", IncludedPaths(result));
    }

    [Fact]
    public void ApplySettings_UpdatesModeAndAutomatic()
    {
        var result = IndexCommand.ApplySettings(SamplePolicy, "none", false);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("none", doc.RootElement.GetProperty("indexingMode").GetString());
        Assert.False(doc.RootElement.GetProperty("automatic").GetBoolean());
    }

    [Fact]
    public void ApplySettings_LeavesUnspecifiedPropertiesUnchanged()
    {
        var result = IndexCommand.ApplySettings(SamplePolicy, mode: null, automatic: null);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("consistent", doc.RootElement.GetProperty("indexingMode").GetString());
        Assert.True(doc.RootElement.GetProperty("automatic").GetBoolean());
    }

    [Theory]
    [InlineData("consistent", "consistent")]
    [InlineData("none", "none")]
    [InlineData("Consistent", "consistent")]
    [InlineData(" NONE ", "none")]
    public void ApplySettings_NormalizesModeCasing(string mode, string expected)
    {
        var result = IndexCommand.ApplySettings(SamplePolicy, mode, automatic: null);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(expected, doc.RootElement.GetProperty("indexingMode").GetString());
    }

    [Theory]
    [InlineData("lazy")]
    [InlineData("consistnet")]
    [InlineData("off")]
    public void ApplySettings_ThrowsForInvalidMode(string mode)
    {
        Assert.Throws<CommandException>(() => IndexCommand.ApplySettings(SamplePolicy, mode, automatic: null));
    }

    [Theory]
    [InlineData("consistent", "consistent")]
    [InlineData("None", "none")]
    public void NormalizeMode_NormalizesKnownModes(string value, string expected)
    {
        Assert.Equal(expected, IndexCommand.NormalizeMode(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeMode_ReturnsNullForEmptyValues(string? value)
    {
        Assert.Null(IndexCommand.NormalizeMode(value));
    }

    [Fact]
    public void NormalizeMode_ThrowsForInvalidValue()
    {
        Assert.Throws<CommandException>(() => IndexCommand.NormalizeMode("lazy"));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    [InlineData(" false ", false)]
    public void ParseAutomatic_ParsesBooleanValues(string value, bool expected)
    {
        Assert.Equal(expected, IndexCommand.ParseAutomatic(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseAutomatic_ReturnsNullForEmptyValues(string? value)
    {
        Assert.Null(IndexCommand.ParseAutomatic(value));
    }

    [Fact]
    public void ParseAutomatic_ThrowsForInvalidValue()
    {
        Assert.Throws<CommandException>(() => IndexCommand.ParseAutomatic("yes"));
    }

    private static List<string?> IncludedPaths(string json) => PathsOf(json, "includedPaths");

    private static List<string?> ExcludedPaths(string json) => PathsOf(json, "excludedPaths");

    private static List<string?> PathsOf(string json, string property)
    {
        using var doc = JsonDocument.Parse(json);
        var paths = new List<string?>();
        if (doc.RootElement.TryGetProperty(property, out var array))
        {
            foreach (var item in array.EnumerateArray())
            {
                paths.Add(item.GetProperty("path").GetString());
            }
        }

        return paths;
    }
}
