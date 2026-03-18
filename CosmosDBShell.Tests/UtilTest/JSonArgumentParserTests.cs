// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.ArgumentParser;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Util;

using Xunit;

namespace CosmosShell.Tests.UtilTest;

public class JSonArgumentParserTests
{
    [Fact]
    public void Parse_EmptyString_ReturnsEmptyList()
    {
        // Arrange
        string path = "";

        // Act
        var instructions = JsonOperationParser.Parse(path);

        // Assert
        Assert.Empty(instructions);
    }

    [Fact]
    public void Parse_SingleProperty_ReturnsPropertyAccess()
    {
        // Arrange
        string path = "name";

        // Act
        var instructions = JsonOperationParser.Parse(path);

        // Assert
        Assert.Single(instructions);
        Assert.IsType<PropertyAccess>(instructions[0]);
        Assert.Equal("name", ((PropertyAccess)instructions[0]).PropertyName);
    }

    [Fact]
    public void Parse_NestedProperties_ReturnsMultiplePropertyAccess()
    {
        // Arrange
        string path = "user.address.city";

        // Act
        var instructions = JsonOperationParser.Parse(path);

        // Assert
        Assert.Equal(3, instructions.Count);
        Assert.Equal("user", ((PropertyAccess)instructions[0]).PropertyName);
        Assert.Equal("address", ((PropertyAccess)instructions[1]).PropertyName);
        Assert.Equal("city", ((PropertyAccess)instructions[2]).PropertyName);
    }

    [Fact]
    public void Parse_ArrayIndex_ReturnsArrayAccess()
    {
        // Arrange
        string path = "items[0]";

        // Act
        var instructions = JsonOperationParser.Parse(path);

        // Assert
        Assert.Equal(2, instructions.Count);
        Assert.Equal("items", ((PropertyAccess)instructions[0]).PropertyName);
        Assert.Equal(0, ((ArrayAccess)instructions[1]).Index);
    }

    [Fact]
    public void Parse_ComplexPath_ReturnsMixedInstructions()
    {
        // Arrange
        string path = "data.users[1].address.street";

        // Act
        var instructions = JsonOperationParser.Parse(path);

        // Assert
        Assert.Equal(5, instructions.Count);
        Assert.Equal("data", ((PropertyAccess)instructions[0]).PropertyName);
        Assert.Equal("users", ((PropertyAccess)instructions[1]).PropertyName);
        Assert.Equal(1, ((ArrayAccess)instructions[2]).Index);
        Assert.Equal("address", ((PropertyAccess)instructions[3]).PropertyName);
        Assert.Equal("street", ((PropertyAccess)instructions[4]).PropertyName);
    }

    [Fact]
    public void Evaluate_SimpleProperty_ReturnsPropertyValue()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"{""name"": ""John"", ""age"": 30}");
        string path = "name";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal("John", result.GetString());
    }

    [Fact]
    public void Evaluate_NestedProperty_ReturnsNestedValue()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"{""user"": {""name"": ""Jane"", ""email"": ""jane@example.com""}}");
        string path = "user.email";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal("jane@example.com", result.GetString());
    }

    [Fact]
    public void Evaluate_ArrayAccess_ReturnsArrayElement()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"{""items"": [""apple"", ""banana"", ""orange""]}");
        string path = "items[1]";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal("banana", result.GetString());
    }

    [Fact]
    public void Evaluate_ComplexPath_ReturnsCorrectValue()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""users"": [
                {""name"": ""User1"", ""addresses"": [{""city"": ""NYC""}, {""city"": ""LA""}]},
                {""name"": ""User2"", ""addresses"": [{""city"": ""Chicago""}, {""city"": ""Boston""}]}
            ]
        }");
        string path = "users[1].addresses[0].city";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal("Chicago", result.GetString());
    }

    [Fact]
    public void Evaluate_InvalidPath_ThrowsPropertyNotFoundException()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"{""name"": ""John""}");
        string path = "nonexistent.property";

        Assert.Throws<PropertyNotFoundException>(() => JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path));
    }

    [Fact]
    public void Evaluate_ArrayIndexOutOfBounds_ThrowsException()
    {
        var json = JsonSerializer.Deserialize<JsonElement>(@"{""items"": [""one"", ""two""]}");
        string path = "items[5]";
        Assert.Throws<IndexOutOfRangeException>(() => JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path));
    }

    [Fact]
    public void Parse_InvalidArrayIndex_ThrowsException()
    {
        string path = "items[abc]";
        Assert.Throws<ArgumentException>(() => JsonOperationParser.Parse(path));
    }

    [Fact]
    public void Parse_UnclosedBracket_ThrowsException()
    {
        // Arrange
        string path = "items[0";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => JsonOperationParser.Parse(path));
        Assert.Contains("Unclosed array bracket", exception.Message);
    }

    [Fact]
    public void Parse_LeadingDot_IgnoresDot()
    {
        // Arrange
        string path = ".name";

        // Act
        var instructions = JsonOperationParser.Parse(path);

        // Assert
        Assert.Single(instructions);
        Assert.Equal("name", ((PropertyAccess)instructions[0]).PropertyName);
    }

    [Fact]
    public void Evaluate_PipeSimple_ChainsOperations()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""user"": {
                ""name"": ""John"",
                ""profile"": {
                    ""email"": ""john@example.com""
                }
            }
        }");
        string path = ".user | .profile";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Equal("john@example.com", result.GetProperty("email").GetString());
    }

    [Fact]
    public void Evaluate_PipeWithArrayAccess_ProcessesSequentially()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""data"": {
                ""items"": [
                    {""id"": 1, ""name"": ""First""},
                    {""id"": 2, ""name"": ""Second""}
                ]
            }
        }");
        string path = "data | items[1] | name";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal("Second", result.GetString());
    }

    [Fact]
    public void Evaluate_PipeComplexPath_ReturnsCorrectValue()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""company"": {
                ""departments"": [
                    {
                        ""name"": ""IT"",
                        ""employees"": [
                            {""name"": ""Alice"", ""role"": ""Developer""},
                            {""name"": ""Bob"", ""role"": ""Manager""}
                        ]
                    }
                ]
            }
        }");
        string path = "company | departments[0] | employees[1] | role";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal("Manager", result.GetString());
    }

    [Fact]
    public void Evaluate_PipeWithWhitespace_HandlesCorrectly()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"{""a"": {""b"": {""c"": ""value""}}}");
        string path = "a  |  b  |  c";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal("value", result.GetString());
    }

    [Fact]
    public void Evaluate_PipeWithMixedNotation_ProcessesCorrectly()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""users"": [
                {
                    ""profile"": {
                        ""settings"": {
                            ""theme"": ""dark""
                        }
                    }
                }
            ]
        }");
        string path = "users[0].profile | settings.theme";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal("dark", result.GetString());
    }

    [Fact]
    public void Evaluate_PipeWithInvalidPath_ThrowsException()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"{""a"": {""b"": ""value""}}");
        string path = ".a | .c | .d";

        // Act & Assert
        Assert.Throws<PropertyNotFoundException>(() => JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path));
    }

    [Fact]
    public void Evaluate_PipeArrayToProperty_ThrowsException()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"{""items"": [1, 2, 3]}");
        string path = ".items | .name";

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path));
    }

    [Fact]
    public void Evaluate_EmptyPipeSegment_IgnoresEmpty()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"{""a"": {""b"": ""value""}}");
        string path = "a | | b";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal("value", result.GetString());
    }

    [Fact]
    public void Evaluate_PipeAtStart_IgnoresLeadingPipe()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"{""name"": ""test""}");
        string path = "| name";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal("test", result.GetString());
    }

    [Fact]
    public void Evaluate_PipeAtEnd_IgnoresTrailingPipe()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"{""name"": ""test""}");
        string path = "name |";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal("test", result.GetString());
    }

    [Fact]
    public void Evaluate_MultiplePipes_ProcessesAllSegments()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""level1"": {
                ""level2"": {
                    ""level3"": {
                        ""level4"": {
                            ""value"": ""deep""
                        }
                    }
                }
            }
        }");
        string path = "level1 | level2 | level3 | level4 | value";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal("deep", result.GetString());
    }

    [Fact]
    public void Evaluate_PipeWithBracketsInSegment_ParsesCorrectly()
    {
        // Arrange
        var json = JsonSerializer.Deserialize<JsonElement>(@"
        {
            ""data"": {
                ""items[0]"": {
                    ""value"": ""special key""
                }
            }
        }");
        string path = @".data | .items\[0\]";

        // Act
        var result = JsonOperationParser.Evaluate(ShellInterpreter.Instance, new CommandState { Result = new ShellJson(json) }, path);

        // Assert
        Assert.Equal("special key", result.GetProperty("value").GetString());
    }
}