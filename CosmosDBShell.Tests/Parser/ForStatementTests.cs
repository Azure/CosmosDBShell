// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System;
using System.Threading.Tasks;
using System.Text.Json;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using Xunit;

public class ForStatementTests : TestBase
{
    [Fact]
    public void ParseBasicForStatement()
    {
        // Arrange
        var script = "for $i in [1, 2, 3] echo $i";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();

        // Assert
        Assert.Single(statements);
        var forStmt = Assert.IsType<ForStatement>(statements[0]);
        Assert.Equal("i", forStmt.VariableName);
        Assert.NotNull(forStmt.Collection);
        Assert.NotNull(forStmt.Statement);
    }

    [Fact]
    public void ParseForStatementWithBlock()
    {
        // Arrange
        var script = "for $item in $collection { echo $item; echo \"done\" }";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();

        // Assert
        Assert.Single(statements);
        var forStmt = Assert.IsType<ForStatement>(statements[0]);
        Assert.Equal("item", forStmt.VariableName);
        var blockStmt = Assert.IsType<BlockStatement>(forStmt.Statement);
        Assert.Equal(2, blockStmt.Statements.Count);
    }

    [Fact]
    public async Task ExecuteForStatement_IteratesOverArray()
    {
        // Arrange
        var script = @"
            $result = []
            for $i in [1, 2, 3] {
                $result = $result + [$i]
            }
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var result = GetVariable("result");
        var json = Assert.IsType<ShellJson>(result);
        var array = json.Value.EnumerateArray().ToArray();
        Assert.Equal(3, array.Length);
        Assert.Equal(1, array[0].GetInt32());
        Assert.Equal(2, array[1].GetInt32());
        Assert.Equal(3, array[2].GetInt32());
    }

    [Fact]
    public async Task ExecuteForStatement_WithBreak_ExitsEarly()
    {
        // Arrange
        var script = @"
            $result = []
            for $i in [1, 2, 3, 4, 5] {
                if $i == 3 break
                $result = $result + [$i]
            }
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var result = GetVariable("result");
        var json = Assert.IsType<ShellJson>(result);
        var array = json.Value.EnumerateArray().ToArray();
        Assert.Equal(2, array.Length);
        Assert.Equal(1, array[0].GetInt32());
        Assert.Equal(2, array[1].GetInt32());
    }

    [Fact]
    public async Task ExecuteForStatement_WithContinue_SkipsIteration()
    {
        // Arrange
        var script = @"
            $result = []
            for $i in [1, 2, 3, 4, 5] {
                if $i == 3 continue
                $result = $result + [$i]
            }
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var result = GetVariable("result");
        var json = Assert.IsType<ShellJson>(result);
        var array = json.Value.EnumerateArray().ToArray();
        Assert.Equal(4, array.Length);
        Assert.Equal(1, array[0].GetInt32());
        Assert.Equal(2, array[1].GetInt32());
        Assert.Equal(4, array[2].GetInt32());
        Assert.Equal(5, array[3].GetInt32());
    }

    [Fact]
    public async Task ExecuteForStatement_NestedWithBreak_OnlyBreaksInnerLoop()
    {
        // Arrange
        var script = @"
            $result = []
            for $i in [1, 2] {
                for $j in [10, 20, 30] {
                    if $j == 20 break
                    $result = $result + [$i * 100 + $j]
                }
            }
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var result = GetVariable("result");
        var json = Assert.IsType<ShellJson>(result);
        var array = json.Value.EnumerateArray().ToArray();
        Assert.Equal(2, array.Length);
        Assert.Equal(110, array[0].GetInt32());
        Assert.Equal(210, array[1].GetInt32());
    }

    [Fact]
    public async Task ExecuteForStatement_EmptyCollection_DoesNotExecuteBody()
    {
        // Arrange
        var script = @"
            $executed = false
            for $i in [] {
                $executed = true
            }
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var executed = GetVariable("executed");
        Assert.IsType<ShellBool>(executed);
        Assert.False(((ShellBool)executed).Value);
    }

    [Fact]
    public async Task ExecuteForStatement_ModifyLoopVariable_DoesNotAffectIteration()
    {
        // Arrange
        var script = @"
            $result = []
            for $i in [1, 2, 3] {
                $result = $result + [$i]
                $i = 100
            }
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var result = GetVariable("result");
        var json = Assert.IsType<ShellJson>(result);
        var array = json.Value.EnumerateArray().ToArray();
        Assert.Equal(3, array.Length);
        Assert.Equal(1, array[0].GetInt32());
        Assert.Equal(2, array[1].GetInt32());
        Assert.Equal(3, array[2].GetInt32());
    }

    [Fact]
    public void ParseForStatement_WithCommandExpression_ParsesCorrectly()
    {
        // Arrange - for loop with a command in parentheses as the collection
        var script = @"for $file in (dir ""exesample*"") {
    echo ""File: "" $file.name
}";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();

        // Assert
        Assert.Single(statements);
        var forStmt = Assert.IsType<ForStatement>(statements[0]);
        Assert.Equal("file", forStmt.VariableName);

        // The collection should be a ParensExpression containing a CommandExpression
        var parensExpr = Assert.IsType<ParensExpression>(forStmt.Collection);
        var cmdExpr = Assert.IsType<CommandExpression>(parensExpr.InnerExpression); Assert.Equal("dir", cmdExpr.Name); Assert.Single(cmdExpr.Arguments);

        // The body should be a block with an echo command
        var blockStmt = Assert.IsType<BlockStatement>(forStmt.Statement);
        Assert.Single(blockStmt.Statements);
        var echoCmd = Assert.IsType<CommandStatement>(blockStmt.Statements[0]);
        Assert.Equal("echo", echoCmd.Name);
    }

    [Fact]
    public void ParseForStatement_WithCommandExpressionAndPropertyAccess_ParsesCorrectly()
    {
        // Arrange - for loop accessing a property on each item
        var script = @"for $f in (dir ""*.json"") { echo $f.name }";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();

        // Assert
        Assert.Single(statements);
        var forStmt = Assert.IsType<ForStatement>(statements[0]);
        Assert.Equal("f", forStmt.VariableName);

        // The collection should be a ParensExpression
        Assert.IsType<ParensExpression>(forStmt.Collection);

        // The body should have an echo command
        var blockStmt = Assert.IsType<BlockStatement>(forStmt.Statement);
        Assert.Single(blockStmt.Statements);
        var echoCmd = Assert.IsType<CommandStatement>(blockStmt.Statements[0]);
        Assert.Equal("echo", echoCmd.Name);

        // The echo command should have an argument with property access ($f.name)
        Assert.Single(echoCmd.Arguments);
    }
}


