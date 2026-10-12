// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;
using Microsoft.Azure.Cosmos;

public class PrintCommandTests
{
    [Theory]
    [InlineData("680")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[1001,\"customer-1001\"]")]
    [InlineData("\"680\"")]
    [InlineData("")]
    [InlineData(" ")]
    public void PositionalKey_PreservesStringValue(string value)
    {
        var command = new PrintCommand { PartitionKey = value };

        Assert.Equal(new PartitionKey(value).ToString(), command.GetPartitionKey().ToString());
    }

    [Theory]
    [InlineData("680", "[680]")]
    [InlineData("680.5", "[680.5]")]
    [InlineData("\"680\"", "[\"680\"]")]
    [InlineData("true", "[true]")]
    [InlineData("false", "[false]")]
    [InlineData("null", "[null]")]
    [InlineData("\"true\"", "[\"true\"]")]
    [InlineData("\"null\"", "[\"null\"]")]
    [InlineData("customer-1", "[\"customer-1\"]")]
    [InlineData("001", "[\"001\"]")]
    [InlineData("-svc", "[\"-svc\"]")]
    [InlineData("", "[\"\"]")]
    [InlineData(" ", "[\" \"]")]
    [InlineData("[1001,\"customer-1001\"]", "[1001,\"customer-1001\"]")]
    [InlineData("[\"tenant-1\",680,true]", "[\"tenant-1\",680,true]")]
    [InlineData("[\"tenant-1\",null,false]", "[\"tenant-1\",null,false]")]
    public void TypedKey_PreservesTypesAndOrder(string value, string expected)
    {
        var command = new PrintCommand { PartitionKeyArgument = value };
        using var expectedJson = JsonDocument.Parse(expected);
        using var actualJson = JsonDocument.Parse(command.GetPartitionKey().ToString());

        Assert.True(JsonElement.DeepEquals(expectedJson.RootElement, actualJson.RootElement));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{},1]")]
    [InlineData("[[1],2]")]
    [InlineData("[1,]")]
    [InlineData("\"unterminated")]
    [InlineData("[]")]
    [InlineData("[1,2,3,4]")]
    [InlineData("1e400")]
    [InlineData("[\"tenant\",1e400]")]
    public void TypedKey_InvalidJsonOrComponents_ThrowsInputError(string value)
    {
        var command = new PrintCommand { PartitionKeyArgument = value };

        var exception = Assert.Throws<CommandException>(() => command.GetPartitionKey());

        Assert.Equal(MessageService.GetString("command-print-error-invalid_pk_json"), exception.Message);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Fact]
    public void MissingKey_ThrowsInputError()
    {
        var exception = Assert.Throws<CommandException>(() => new PrintCommand().GetPartitionKey());

        Assert.Equal(MessageService.GetString("command-print-error-missing_key"), exception.Message);
    }

    [Theory]
    [InlineData("680")]
    [InlineData("")]
    public void BothKeyForms_ThrowsInputError(string positional)
    {
        var command = new PrintCommand { PartitionKey = positional, PartitionKeyArgument = "680" };

        var exception = Assert.Throws<CommandException>(() => command.GetPartitionKey());

        Assert.Equal(MessageService.GetString("command-print-error-conflicting_keys"), exception.Message);
    }

    [Theory]
    [InlineData("680", 2)]
    [InlineData("[1001]", 2)]
    [InlineData("[1001,\"customer-1001\",true]", 2)]
    [InlineData("[\"tenant\",680]", 3)]
    public void IncompleteOrExtraComponents_ThrowsInputError(string value, int expected)
    {
        var key = new PrintCommand { PartitionKeyArgument = value }.GetPartitionKey();

        var exception = Assert.Throws<CommandException>(() => PrintCommand.ValidatePartitionKeyComponentCount(key, expected));

        Assert.Contains($"all {expected} components", exception.Message);
    }

    [Theory]
    [InlineData("680", 1)]
    [InlineData("null", 1)]
    [InlineData("[1001,\"customer-1001\"]", 2)]
    [InlineData("[\"tenant\",680,true]", 3)]
    public void CompleteKey_AcceptsMatchingComponentCount(string value, int expected)
    {
        var key = new PrintCommand { PartitionKeyArgument = value }.GetPartitionKey();

        PrintCommand.ValidatePartitionKeyComponentCount(key, expected);
    }
}
