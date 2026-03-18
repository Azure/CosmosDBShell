// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using Xunit;

namespace CosmosShell.Tests.Parser;

public class LoopStatementTests : TestBase
{
    [Fact]
    public void ParseBasicLoopStatement()
    {
        // Arrange
        var script = "loop echo \"infinite\"";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();

        // Assert
        Assert.Single(statements);
        var loopStmt = Assert.IsType<LoopStatement>(statements[0]);
        Assert.NotNull(loopStmt.Statement);
        Assert.IsType<CommandStatement>(loopStmt.Statement);
    }

    [Fact]
    public void ParseLoopStatementWithBlock()
    {
        // Arrange
        var script = "loop { echo \"running\"; $count = $count + 1 }";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();

        // Assert
        Assert.Single(statements);
        var loopStmt = Assert.IsType<LoopStatement>(statements[0]);
        var blockStmt = Assert.IsType<BlockStatement>(loopStmt.Statement);
        Assert.Equal(2, blockStmt.Statements.Count);
    }

    [Fact]
    public async Task ExecuteLoopStatement_WithBreak_ExitsLoop()
    {
        // Arrange
        var script = @"
            $count = 0
            loop {
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
    public async Task ExecuteLoopStatement_WithContinue_SkipsRestOfIteration()
    {
        // Arrange
        var script = @"
            $count = 0
            $result = []
            loop {
                $count = $count + 1
                if $count == 3 continue
                $result = $result + [$count]
                if $count == 5 break
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
    public async Task ExecuteLoopStatement_NestedLoops()
    {
        // Arrange
        var script = @"
            $result = []
            $i = 0
            loop {
                $j = 0
                loop {
                    $result = $result + [$i * 10 + $j]
                    $j = $j + 1
                    if $j == 3 break
                }
                $i = $i + 1
                if $i == 2 break
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
    public async Task ExecuteLoopStatement_ImmediateBreak()
    {
        // Arrange
        var script = @"
            $executed = 0
            loop {
                break
                $executed = $executed + 1
            }
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var executed = GetVariable("executed");
        var number = Assert.IsType<ShellNumber>(executed);
        Assert.Equal(0, number.Value);
    }

    [Fact]
    public async Task ExecuteLoopStatement_SingleStatement()
    {
        // Arrange
        var script = @"
            $count = 3
            loop if $count == 3 break
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var count = GetVariable("count");
        var number = Assert.IsType<ShellNumber>(count);
        Assert.Equal(3, number.Value);
    }

    [Fact]
    public async Task ExecuteLoopStatement_BreakInNestedIf()
    {
        // Arrange
        var script = @"
            $iterations = 0
            $result = []
            loop {
                $iterations = $iterations + 1
                if $iterations % 2 == 0 {
                    if $iterations == 6 {
                        break
                    }
                    $result = $result + [$iterations]
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
        Assert.Equal(2, array[0].GetInt32());
        Assert.Equal(4, array[1].GetInt32());
    }

    [Fact]
    public async Task ExecuteLoopStatement_ComplexControlFlow()
    {
        // Arrange
        var script = @"
            $result = []
            $i = 0
            loop {
                $i = $i + 1
                if $i % 3 == 0 continue
                if $i > 10 break
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
        // Should have: 1, 2, 4, 5, 7, 8, 10 (skipping 3, 6, 9)
        Assert.Equal(7, array.Length);
        var expected = new[] { 1, 2, 4, 5, 7, 8, 10 };
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], array[i].GetInt32());
        }
    }
}