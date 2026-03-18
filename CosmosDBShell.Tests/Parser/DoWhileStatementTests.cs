// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Parser;

namespace CosmosShell.Tests.Parser;

public class DoWhileStatementTests : TestBase
{
    [Fact]
    public void ParseBasicDoWhileStatement()
    {
        // Arrange
        var script = "do \n echo $i\nwhile $i < 10";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();

        // Assert
        Assert.Single(statements);
        var doWhileStmt = Assert.IsType<DoWhileStatement>(statements[0]);
        Assert.NotNull(doWhileStmt.Condition);
        Assert.NotNull(doWhileStmt.Statement);
        Assert.IsType<CommandStatement>(doWhileStmt.Statement);
    }

    [Fact]
    public void ParseDoWhileStatementWithBlock()
    {
        // Arrange
        var script = "do { echo $count; $count = $count - 1 } while $count > 0";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();

        // Assert
        Assert.Single(statements);
        var doWhileStmt = Assert.IsType<DoWhileStatement>(statements[0]);
        Assert.NotNull(doWhileStmt.Condition);
        var blockStmt = Assert.IsType<BlockStatement>(doWhileStmt.Statement);
        Assert.Equal(2, blockStmt.Statements.Count);
    }

    [Fact]
    public async Task ExecuteDoWhileStatement_ExecutesAtLeastOnce()
    {
        // Arrange
        var script = @"
            $executed = false
            do {
                $executed = true
            } while false
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var executed = GetVariable("executed");
        Assert.IsType<ShellBool>(executed);
        Assert.True(((ShellBool)executed).Value);
    }

    [Fact]
    public async Task ExecuteDoWhileStatement_RunsUntilConditionFalse()
    {
        // Arrange
        var script = @"
            $i = 0
            $result = []
            do {
                $result = $result + [$i]
                $i = $i + 1
            } while $i < 3
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
    public async Task ExecuteDoWhileStatement_WithBreak_ExitsEarly()
    {
        // Arrange
        var script = @"
            $i = 0
            $result = []
            do {
                if $i == 3 break
                $result = $result + [$i]
                $i = $i + 1
            } while $i < 10
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
    public async Task ExecuteDoWhileStatement_WithContinue_SkipsRestOfIteration()
    {
        // Arrange
        var script = @"
            $i = 0
            $result = []
            do {
                $i = $i + 1
                if $i == 3 continue
                $result = $result + [$i]
            } while $i < 5
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
    public async Task ExecuteDoWhileStatement_NestedLoops()
    {
        // Arrange
        var script = @"
            $result = []
            $i = 0
            do {
                $j = 0
                do {
                    $result = $result + [$i * 10 + $j]
                    $j = $j + 1
                } while $j < 3
                $i = $i + 1
            } while $i < 2
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
    public async Task ExecuteDoWhileStatement_SingleStatement()
    {
        // Arrange
        var script = @"
            $count = 0
            do $count = $count + 1 while $count < 3
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
    public async Task ExecuteDoWhileStatement_ComplexCondition()
    {
        // Arrange
        var script = @"
            $i = 0
            $j = 10
            $result = []
            do {
                $result = $result + [$i]
                $i = $i + 1
                $j = $j - 1
            } while $i < 5 && $j > 5
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

    [Fact]
    public async Task ExecuteDoWhileStatement_BreakInFirstIteration()
    {
        // Arrange
        var script = @"
            $iterations = 0
            do {
                $iterations = $iterations + 1
                break
            } while true
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var iterations = GetVariable("iterations");
        var number = Assert.IsType<ShellNumber>(iterations);
        Assert.Equal(1, number.Value);
    }

    [Fact]
    public async Task ExecuteDoWhileStatement_ContinueInLastIteration()
    {
        // Arrange
        var script = @"
            $i = 0
            $skipped = false
            do {
                $i = $i + 1
                if $i == 3 {
                    $skipped = true
                    continue
                }
            } while $i < 3
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var skipped = GetVariable("skipped");
        Assert.IsType<ShellBool>(skipped);
        Assert.True(((ShellBool)skipped).Value);
    }

    [Fact]
    public async Task ExecuteDoWhileStatement_WithParenthesizedCondition()
    {
        // Arrange
        var script = @"
            $count = 0
            do {
                echo $count
                $count = $count + 1
            } while ($count < 10)
        ";

        // Act
        var state = await RunScriptAsync(script);

        // Assert
        Assert.False(state.IsError);
        var count = GetVariable("count");
        var number = Assert.IsType<ShellNumber>(count);
        Assert.Equal(10, number.Value);
    }
}