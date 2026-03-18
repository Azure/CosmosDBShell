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

public class WhileStatementTests : TestBase
{
    [Fact]
    public void ParseBasicWhileStatement()
    {
        // Arrange
        var script = "while $i < 10 echo $i";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();

        // Assert
        Assert.Single(statements);
        var whileStmt = Assert.IsType<WhileStatement>(statements[0]);
        Assert.NotNull(whileStmt.Condition);
        Assert.NotNull(whileStmt.Statement);
    }

    [Fact]
    public void ParseWhileStatementWithBlock()
    {
        // Arrange
        var script = "while $count > 0 { echo $count; $count = $count - 1 }";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();

        // Assert
        Assert.Single(statements);
        var whileStmt = Assert.IsType<WhileStatement>(statements[0]);
        Assert.NotNull(whileStmt.Condition);
        var blockStmt = Assert.IsType<BlockStatement>(whileStmt.Statement);
        Assert.Equal(2, blockStmt.Statements.Count);
    }

    [Fact]
    public async Task ExecuteWhileStatement_RunsUntilConditionFalse()
    {
        // Arrange
        var script = @"
            $i = 0
            $result = []
            while $i < 3 {
                $result = $result + [$i]
                $i = $i + 1
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
        Assert.Equal(0, array[0].GetInt32());
        Assert.Equal(1, array[1].GetInt32());
        Assert.Equal(2, array[2].GetInt32());
    }

    [Fact]
    public async Task ExecuteWhileStatement_WithBreak_ExitsEarly()
    {
        // Arrange
        var script = @"
            $i = 0
            $result = []
            while $i < 10 {
                if $i == 3 break
                $result = $result + [$i]
                $i = $i + 1
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
        Assert.Equal(0, array[0].GetInt32());
        Assert.Equal(1, array[1].GetInt32());
        Assert.Equal(2, array[2].GetInt32());
    }

    [Fact]
    public async Task ExecuteWhileStatement_WithContinue_SkipsRestOfIteration()
    {
        // Arrange
        var script = @"
            $i = 0
            $result = []
            while $i < 5 {
                $i = $i + 1
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
    public async Task ExecuteWhileStatement_FalseCondition_NeverExecutesBody()
    {
        // Arrange
        var script = @"
            $executed = false
            while false {
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
    public async Task ExecuteWhileStatement_NestedLoops()
    {
        // Arrange
        var script = @"
            $result = []
            $i = 0
            while $i < 2 {
                $j = 0
                while $j < 3 {
                    $result = $result + [$i * 10 + $j]
                    $j = $j + 1
                }
                $i = $i + 1
            }
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var result = GetVariable("result");
        var json = Assert.IsType<ShellJson>(result);
        var array = json.Value.EnumerateArray().ToArray();
        Assert.Equal(6, array.Length);
        Assert.Equal(0, array[0].GetInt32());
        Assert.Equal(1, array[1].GetInt32());
        Assert.Equal(2, array[2].GetInt32());
        Assert.Equal(10, array[3].GetInt32());
        Assert.Equal(11, array[4].GetInt32());
        Assert.Equal(12, array[5].GetInt32());
    }

    [Fact]
    public async Task ExecuteWhileStatement_InfiniteLoopWithBreak()
    {
        // Arrange
        var script = @"
            $count = 0
            while true {
                $count = $count + 1
                if $count == 5 break
            }
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var count = GetVariable("count");
        var number = Assert.IsType<ShellNumber>(count);
        Assert.Equal(5, number.Value);
    }

    [Fact]
    public async Task ExecuteWhileStatement_ComplexCondition()
    {
        // Arrange
        var script = @"
            $i = 0
            $j = 10
            $result = []
            while $i < 5 && $j > 5 {
                $result = $result + [$i]
                $i = $i + 1
                $j = $j - 1
            }
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var result = GetVariable("result");
        var json = Assert.IsType<ShellJson>(result);
        var array = json.Value.EnumerateArray().ToArray();
        Assert.Equal(5, array.Length);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i, array[i].GetInt32());
        }
    }
}