// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Microsoft.Azure.Cosmos;

/// <summary>
/// Unit tests for the pure helpers on <see cref="SprocCommand"/>: subcommand
/// normalization, execution-parameter parsing, and partition-key parsing.
/// </summary>
public class SprocCommandTests
{
    [Theory]
    [InlineData("LIST", "list")]
    [InlineData("  Show  ", "show")]
    [InlineData("Create", "create")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void NormalizeSubcommand_TrimsAndLowercases(string? input, string expected)
    {
        Assert.Equal(expected, SprocCommand.NormalizeSubcommand(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseExecParams_NullOrWhitespace_ReturnsEmpty(string? input)
    {
        Assert.Empty(SprocCommand.ParseExecParams(input));
    }

    [Fact]
    public void ParseExecParams_JsonArray_ReturnsElements()
    {
        var parameters = SprocCommand.ParseExecParams("[\"a\", 1, true]");

        Assert.Equal(3, parameters.Length);
        Assert.Equal("a", ((JsonElement)parameters[0]).GetString());
        Assert.Equal(1, ((JsonElement)parameters[1]).GetInt32());
        Assert.True(((JsonElement)parameters[2]).GetBoolean());
    }

    [Fact]
    public void ParseExecParams_EmptyArray_ReturnsEmpty()
    {
        Assert.Empty(SprocCommand.ParseExecParams("[]"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    [InlineData("\"justAString\"")]
    [InlineData("42")]
    public void ParseExecParams_NonArray_Throws(string input)
    {
        Assert.Throws<CommandException>(() => SprocCommand.ParseExecParams(input));
    }

    [Fact]
    public void ParsePartitionKey_String_MatchesStringPartitionKey()
    {
        Assert.Equal(new PartitionKey("pk1").ToString(), SprocCommand.ParsePartitionKey("pk1").ToString());
    }

    [Fact]
    public void ParsePartitionKey_QuotedString_PreservesStringType()
    {
        Assert.Equal(new PartitionKey("pk1").ToString(), SprocCommand.ParsePartitionKey("\"pk1\"").ToString());
    }

    [Fact]
    public void ParsePartitionKey_Number_PreservesNumericType()
    {
        Assert.Equal(new PartitionKey(42).ToString(), SprocCommand.ParsePartitionKey("42").ToString());
    }

    [Fact]
    public void ParsePartitionKey_Boolean_PreservesBooleanType()
    {
        Assert.Equal(new PartitionKey(true).ToString(), SprocCommand.ParsePartitionKey("true").ToString());
    }

    [Fact]
    public void ParsePartitionKey_JsonArray_BuildsHierarchicalPartitionKey()
    {
        var expected = new PartitionKeyBuilder()
            .Add("tenant")
            .Add("user")
            .Build();

        Assert.Equal(expected.ToString(), SprocCommand.ParsePartitionKey("[\"tenant\",\"user\"]").ToString());
    }

    [Fact]
    public void ParsePartitionKey_JsonObject_Throws()
    {
        Assert.Throws<CommandException>(() => SprocCommand.ParsePartitionKey("{\"a\":1}"));
    }

    [Fact]
    public void DefaultStoredProcedureBody_IsValidSeedTemplate()
    {
        var body = SprocCommand.DefaultStoredProcedureBody();

        Assert.False(string.IsNullOrWhiteSpace(body));
        Assert.Contains("function sample", body);
        Assert.Contains("getContext().getCollection()", body);
        Assert.Contains("queryDocuments", body);
    }
}
