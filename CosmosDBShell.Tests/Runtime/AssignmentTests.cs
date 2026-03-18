// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using Xunit;

namespace CosmosShell.Tests.Runtime;

public class AssignmentTests
{
    private ShellInterpreter CreateInterpreter()
    {
        return new ShellInterpreter();
    }

    [Fact]
    public async Task Assignment_SimpleNumber_StoresCorrectValue()
    {
        // Arrange
        var interpreter = CreateInterpreter();
        var script = "$x = 42";

        // Act
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();

        foreach (var statement in statements)
        {
            await statement.RunAsync(interpreter, new CommandState(), CancellationToken.None);
        }

        // Assert
        var value = interpreter.GetVariable("x");
        Assert.NotNull(value);
        Assert.IsType<ShellNumber>(value);
        Assert.Equal(42, ((ShellNumber)value).Value);
    }

    [Fact]
    public async Task Assignment_SimpleString_StoresCorrectValue()
    {
        // Arrange
        var interpreter = CreateInterpreter();
        var script = "message = \"Hello, World!\"";

        // Act
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();

        foreach (var statement in statements)
        {
            await statement.RunAsync(interpreter, new CommandState(), CancellationToken.None);
        }

        // Assert
        var value = interpreter.GetVariable("message");
        Assert.NotNull(value);
        Assert.IsType<ShellText>(value);
        Assert.Equal("Hello, World!", ((ShellText)value).Text);
    }

    [Fact]
    public async Task Assignment_Boolean_StoresCorrectValue()
    {
        // Arrange
        var interpreter = CreateInterpreter();
        var script = @"
            isActive = true
            isDeleted = false
        ";

        // Act
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();

        foreach (var statement in statements)
        {
            await statement.RunAsync(interpreter, new CommandState(), CancellationToken.None);
        }

        // Assert
        var isActive = interpreter.GetVariable("isActive");
        var isDeleted = interpreter.GetVariable("isDeleted");

        Assert.NotNull(isActive);
        Assert.IsType<ShellBool>(isActive);
        Assert.True(((ShellBool)isActive).Value);

        Assert.NotNull(isDeleted);
        Assert.IsType<ShellBool>(isDeleted);
        Assert.False(((ShellBool)isDeleted).Value);
    }
    /*
    [Fact]
    public async Task Assignment_JsonObject_StoresCorrectValue()
    {
        // Arrange
        var interpreter = CreateInterpreter();
        var script = "$user = { \"name\": \"John\", \"age\": 30 }";

        // Act
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();

        foreach (var statement in statements)
        {
            await statement.RunAsync(interpreter, new CommandState(), CancellationToken.None);
        }

        // Assert
        var value = interpreter.GetVariable("user");
        Assert.NotNull(value);
        Assert.IsType<ShellJson>(value);

        var json = (ShellJson)value;
        Assert.Equal("John", json.Text.GetProperty("name").GetString());
        Assert.Equal(30, json.Text.GetProperty("age").GetInt32());
    }*/

    [Fact]
    public async Task Assignment_JsonArray_StoresCorrectValue()
    {
        // Arrange
        var interpreter = CreateInterpreter();
        var script = "numbers = [1, 2, 3, 4, 5]";

        // Act
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();

        foreach (var statement in statements)
        {
            await statement.RunAsync(interpreter, new CommandState(), CancellationToken.None);
        }

        // Assert
        var value = interpreter.GetVariable("numbers");
        Assert.NotNull(value);
        Assert.IsType<ShellJson>(value);

        var json = (ShellJson)value;
        Assert.Equal(5, json.Value.GetArrayLength());
        Assert.Equal(1, json.Value[0].GetInt32());
        Assert.Equal(5, json.Value[4].GetInt32());
    }

    [Fact]
    public async Task Assignment_ExpressionEvaluation_StoresComputedValue()
    {
        // Arrange
        var interpreter = CreateInterpreter();
        var script = @"
            $a = 10
            $b = 20
            $sum = $a + $b
            $product = $a * $b
        ";

        // Act
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();

        foreach (var statement in statements)
        {
            await statement.RunAsync(interpreter, new CommandState(), CancellationToken.None);
        }

        // Assert
        var sum = interpreter.GetVariable("sum");
        var product = interpreter.GetVariable("product");

        Assert.NotNull(sum);
        Assert.IsType<ShellNumber>(sum);
        Assert.Equal(30, ((ShellNumber)sum).Value);

        Assert.NotNull(product);
        Assert.IsType<ShellNumber>(product);
        Assert.Equal(200, ((ShellNumber)product).Value);
    }

    [Fact]
    public async Task Assignment_StringConcatenation_StoresCorrectValue()
    {
        // Arrange
        var interpreter = CreateInterpreter();
        var script = @"
            $firstName = ""John""
            $lastName = ""Doe""
            $fullName = $firstName + "" "" + $lastName
        ";

        // Act
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();

        foreach (var statement in statements)
        {
            await statement.RunAsync(interpreter, new CommandState(), CancellationToken.None);
        }

        // Assert
        var fullName = interpreter.GetVariable("fullName");
        Assert.NotNull(fullName);
        Assert.IsType<ShellText>(fullName);
        Assert.Equal("John Doe", ((ShellText)fullName).Text);
    }

    [Fact]
    public async Task Assignment_VariableReassignment_UpdatesValue()
    {
        // Arrange
        var interpreter = CreateInterpreter();
        var script = @"
            $counter = 1
            $counter = $counter + 1
            $counter = $counter * 2
        ";

        // Act
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();

        foreach (var statement in statements)
        {
            await statement.RunAsync(interpreter, new CommandState(), CancellationToken.None);
        }

        // Assert
        var counter = interpreter.GetVariable("counter");
        Assert.NotNull(counter);
        Assert.IsType<ShellNumber>(counter);
        Assert.Equal(4, ((ShellNumber)counter).Value); // (1 + 1) * 2 = 4
    }

    [Fact]
    public async Task Assignment_ComplexExpression_EvaluatesCorrectly()
    {
        // Arrange
        var interpreter = CreateInterpreter();
        var script = @"
            $x = 5
            $y = 3
            $result = ($x + $y) * 2 - 4
        ";

        // Act
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();

        foreach (var statement in statements)
        {
            await statement.RunAsync(interpreter, new CommandState(), CancellationToken.None);
        }

        // Assert
        var result = interpreter.GetVariable("result");
        Assert.NotNull(result);
        Assert.IsType<ShellNumber>(result);
        Assert.Equal(12, ((ShellNumber)result).Value); // (5 + 3) * 2 - 4 = 12
    }

    [Fact]
    public async Task Assignment_UndefinedVariable_ThrowsException()
    {
        // Arrange
        var interpreter = CreateInterpreter();
        var script = "result = $undefinedVar + 10";

        // Act & Assert
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();

        await Assert.ThrowsAsync<ShellException>(async () =>
        {
            foreach (var statement in statements)
            {
                await statement.RunAsync(interpreter, new CommandState(), CancellationToken.None);
            }
        });
    }

    [Fact]
    public async Task Assignment_ChainedAssignments_AllVariablesSet()
    {
        // Arrange
        var interpreter = CreateInterpreter();
        var script = @"
            $a = 10
            $b = $a
            $c = $b
        ";

        // Act
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();

        foreach (var statement in statements)
        {
            await statement.RunAsync(interpreter, new CommandState(), CancellationToken.None);
        }

        // Assert
        var a = interpreter.GetVariable("a");
        var b = interpreter.GetVariable("b");
        var c = interpreter.GetVariable("c");

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotNull(c);

        Assert.IsType<ShellNumber>(a);
        Assert.IsType<ShellNumber>(b);
        Assert.IsType<ShellNumber>(c);

        Assert.Equal(10, ((ShellNumber)a).Value);
        Assert.Equal(10, ((ShellNumber)b).Value);
        Assert.Equal(10, ((ShellNumber)c).Value);
    }
    /*
    [Fact]
    public async Task Assignment_MixedTypes_PreservesTypes()
    {
        // Arrange
        var interpreter = CreateInterpreter();
        var script = @"
            $name = ""Alice""
            $age = 25
            $isStudent = true
            $grades = [90, 85, 92]
            $profile = { ""city"": ""Seattle"", ""country"": ""USA"" }
        ";

        // Act
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();

        foreach (var statement in statements)
        {
            await statement.RunAsync(interpreter, new CommandState(), CancellationToken.None);
        }

        // Assert
        Assert.IsType<ShellText>(interpreter.GetVariable("name"));
        Assert.IsType<ShellNumber>(interpreter.GetVariable("age"));
        Assert.IsType<ShellBool>(interpreter.GetVariable("isStudent"));
        Assert.IsType<ShellJson>(interpreter.GetVariable("grades"));
        Assert.IsType<ShellJson>(interpreter.GetVariable("profile"));
    }*/
}