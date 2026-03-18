// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using Xunit;

namespace CosmosShell.Tests.Parser;

public class JsonExpressionTests
{
    private static Expression ParseExpression(string input)
    {
        var lexer = new Lexer(input);
        var parser = new ExpressionParser(lexer);
        return parser.ParseExpression();
    }

    private async Task<ShellObject> EvaluateExpressionAsync(string input)
    {
        var expression = ParseExpression(input);
        return await expression.EvaluateAsync(ShellInterpreter.Instance, new CommandState(), CancellationToken.None);
    }

#pragma warning disable CS0618, VSTHRD002 // Type or member is obsolete, Synchronously waiting on tasks
    private ShellObject EvaluateExpression(string input)
    {
        var expression = ParseExpression(input);
        return expression.EvaluateAsync(ShellInterpreter.Instance, new CommandState(), CancellationToken.None).GetAwaiter().GetResult();
    }
#pragma warning restore CS0618, VSTHRD002

    [Fact]
    public void ParseEmptyObject_ReturnsJsonExpressionWithNoProperties()
    {
        // Arrange & Act
        var expr = ParseExpression("{}");

        // Assert
        var jsonExpr = Assert.IsType<JsonExpression>(expr);
        Assert.Empty(jsonExpr.Properties);
    }

    [Fact]
    public void ParseSimpleObject_WithStringProperties()
    {
        // Arrange & Act
        var expr = ParseExpression("{\"name\": \"John\", \"city\": \"Seattle\"}");

        // Assert
        var jsonExpr = Assert.IsType<JsonExpression>(expr);
        Assert.Equal(2, jsonExpr.Properties.Count);

        // Verify keys
        var keys = jsonExpr.Properties.Keys.Cast<ShellText>().Select(k => k.Text).ToList();
        Assert.Contains("name", keys);
        Assert.Contains("city", keys);

        // Verify values are constant expressions
        foreach (var value in jsonExpr.Properties.Values)
        {
            Assert.IsType<ConstantExpression>(value);
        }
    }

    [Fact]
    public void ParseObjectWithUnquotedKeys()
    {
        // Arrange & Act
        var expr = ParseExpression("{name: \"John\", age: 30}");

        // Assert
        var jsonExpr = Assert.IsType<JsonExpression>(expr);
        Assert.Equal(2, jsonExpr.Properties.Count);

        var keys = jsonExpr.Properties.Keys.Cast<ShellText>().Select(k => k.Text).ToList();
        Assert.Contains("name", keys);
        Assert.Contains("age", keys);
    }

    [Fact]
    public void ParseObjectWithExpressions()
    {
        // Arrange & Act
        var expr = ParseExpression("{sum: 1 + 2, concat: \"Hello\" + \" World\", flag: true && false}");

        // Assert
        var jsonExpr = Assert.IsType<JsonExpression>(expr);
        Assert.Equal(3, jsonExpr.Properties.Count);

        // Verify expressions
        var sumExpr = jsonExpr.Properties.Values.ElementAt(0);
        Assert.IsType<BinaryOperatorExpression>(sumExpr);

        var concatExpr = jsonExpr.Properties.Values.ElementAt(1);
        Assert.IsType<BinaryOperatorExpression>(concatExpr);

        var flagExpr = jsonExpr.Properties.Values.ElementAt(2);
        Assert.IsType<BinaryOperatorExpression>(flagExpr);
    }

    [Fact]
    public void ParseNestedObjects()
    {
        // Arrange & Act
        var expr = ParseExpression("{user: {name: \"John\", age: 30}, active: true}");

        // Assert
        var jsonExpr = Assert.IsType<JsonExpression>(expr);
        Assert.Equal(2, jsonExpr.Properties.Count);

        // Check nested object
        var userValue = jsonExpr.Properties.Values.First();
        Assert.IsType<JsonExpression>(userValue);
    }

    [Fact]
    public void ParseObjectWithArrayValues()
    {
        // Arrange & Act
        var expr = ParseExpression("{numbers: [1, 2, 3], names: [\"Alice\", \"Bob\"]}");

        // Assert
        var jsonExpr = Assert.IsType<JsonExpression>(expr);
        Assert.Equal(2, jsonExpr.Properties.Count);

        // Check array values
        foreach (var value in jsonExpr.Properties.Values)
        {
            Assert.IsType<JsonArrayExpression>(value);
        }
    }

    [Fact]
    public void ParseObjectWithTrailingComma()
    {
        // Arrange & Act
        var expr = ParseExpression("{name: \"John\", age: 30,}");

        // Assert
        var jsonExpr = Assert.IsType<JsonExpression>(expr);
        Assert.Equal(2, jsonExpr.Properties.Count);
    }

    [Fact]
    public void ParseObjectWithWhitespace()
    {
        // Arrange & Act
        var expr = ParseExpression(@"{
            name: ""John"",
            age: 30,
            city: ""Seattle""
        }");

        // Assert
        var jsonExpr = Assert.IsType<JsonExpression>(expr);
        Assert.Equal(3, jsonExpr.Properties.Count);
    }

    [Fact]
    public async Task EvaluateJsonExpression_ProducesCorrectJson()
    {
        // Arrange
        var expr = ParseExpression("{name: \"John\", age: 25 + 5, active: true}");
        var jsonExpr = Assert.IsType<JsonExpression>(expr);
        var interpreter = new ShellInterpreter();
        var state = new CommandState();

        // Act
        var result = await jsonExpr.EvaluateAsync(interpreter, state, CancellationToken.None);

        // Assert
        var shellJson = Assert.IsType<ShellJson>(result);
        Assert.Equal(JsonValueKind.Object, shellJson.Value.ValueKind);

        Assert.Equal("John", shellJson.Value.GetProperty("name").GetString());
        Assert.Equal(30, shellJson.Value.GetProperty("age").GetInt32());
        Assert.True(shellJson.Value.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task EvaluateJsonExpression_WithVariables()
    {
        // Arrange
        var interpreter = new ShellInterpreter();
        interpreter.SetVariable("userName", new ShellText("Alice"));
        interpreter.SetVariable("userAge", new ShellNumber(25));

        var expr = ParseExpression("{name: $userName, age: $userAge}");
        var jsonExpr = Assert.IsType<JsonExpression>(expr);
        var state = new CommandState();

        // Act
        var result = await jsonExpr.EvaluateAsync(interpreter, state, CancellationToken.None);

        // Assert
        var shellJson = Assert.IsType<ShellJson>(result);
        Assert.Equal("Alice", shellJson.Value.GetProperty("name").GetString());
        Assert.Equal(25, shellJson.Value.GetProperty("age").GetInt32());
    }

    [Fact]
    public async Task EvaluateJsonExpression_ComplexNested()
    {
        // Arrange
        var expr = ParseExpression(@"{
            user: {
                name: ""John"",
                scores: [85, 90, 95],
                average: (85 + 90 + 95) / 3
            },
            metadata: {
                created: ""2024-01-01"",
                version: 1
            }
        }");
        var jsonExpr = Assert.IsType<JsonExpression>(expr);
        var interpreter = new ShellInterpreter();
        var state = new CommandState();

        // Act
        var result = await jsonExpr.EvaluateAsync(interpreter, state, CancellationToken.None);

        // Assert
        var shellJson = Assert.IsType<ShellJson>(result);
        var user = shellJson.Value.GetProperty("user");
        Assert.Equal("John", user.GetProperty("name").GetString());

        var scores = user.GetProperty("scores");
        Assert.Equal(3, scores.GetArrayLength());
        Assert.Equal(85, scores[0].GetInt32());
        Assert.Equal(90, scores[1].GetInt32());
        Assert.Equal(95, scores[2].GetInt32());

        Assert.Equal(90, user.GetProperty("average").GetInt32());

        var metadata = shellJson.Value.GetProperty("metadata");
        Assert.Equal("2024-01-01", metadata.GetProperty("created").GetString());
        Assert.Equal(1, metadata.GetProperty("version").GetInt32());
    }

    [Fact]
    public void EvaluateExpression_NestedJsonObject_ReturnsShellJson()
    {
        // Act
        var result = EvaluateExpression("{ \"user\": { \"name\": \"Alice\", \"roles\": [\"admin\", \"user\"] } }");

        // Assert
        Assert.IsType<ShellJson>(result);
        var json = (ShellJson)result;
        var user = json.Value.GetProperty("user");
        Assert.Equal("Alice", user.GetProperty("name").GetString());
        var roles = user.GetProperty("roles");
        Assert.Equal(2, roles.GetArrayLength());
        Assert.Equal("admin", roles[0].GetString());
        Assert.Equal("user", roles[1].GetString());
    }

    [Fact]
    public void EvaluateExpression_JsonWithBooleanAndNull_ReturnsShellJson()
    {
        // Act
        var result = EvaluateExpression("{ \"active\": true, \"deleted\": false, \"metadata\": null }");

        // Assert
        Assert.IsType<ShellJson>(result);
        var json = (ShellJson)result;
        Assert.True(json.Value.GetProperty("active").GetBoolean());
        Assert.False(json.Value.GetProperty("deleted").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.Value.GetProperty("metadata").ValueKind);
    }

    [Fact]
    public void EvaluateExpression_JsonWithNumbers_ReturnsShellJson()
    {
        // Act
        var result = EvaluateExpression("{ \"integer\": 42, \"decimal\": 3.14 }");

        // Assert
        Assert.IsType<ShellJson>(result);
        var json = (ShellJson)result;
        Assert.Equal(42, json.Value.GetProperty("integer").GetInt32());
        Assert.Equal(3.14, json.Value.GetProperty("decimal").GetDouble());
    }

    [Fact]
    public void ParseExpression_JsonWithIdentifiersAsKeys_ReturnsJsonExpression()
    {
        // Act - JSON often allows unquoted keys in relaxed parsers
        var result = EvaluateExpression("{ \"name\": \"John\", \"age\": 30 }");

        // Assert
        Assert.IsType<ShellJson>(result);
        var json = (ShellJson)result;
        Assert.Equal("John", json.Value.GetProperty("name").GetString());
        Assert.Equal(30, json.Value.GetProperty("age").GetInt32());
    }

    [Fact]
    public void ParseExpression_EmptyJsonObject_ReturnsJsonExpression()
    {
        // Act
        var result = EvaluateExpression("{}");

        // Assert
        Assert.IsType<ShellJson>(result);
        var json = (ShellJson)result;
        Assert.Equal(System.Text.Json.JsonValueKind.Object, json.Value.ValueKind);
        Assert.Empty(json.Value.EnumerateObject());
    }
}