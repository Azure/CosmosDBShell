// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Executes whole scripts to drive the <c>RunAsync</c> branches of control-flow
/// statements (if/else, while, do-while, for, loop, break, continue, return, blocks)
/// together with the condition-evaluation paths in the expression parser.
/// </summary>
public class StatementExecutionTests : TestBase
{
    private int GetInt(string name)
    {
        var value = GetVariable(name);
        return (int)Assert.IsType<ShellNumber>(value).Value;
    }

    [Fact]
    public async Task If_TrueCondition_ExecutesThenBranch()
    {
        var state = await RunScriptAsync("$x = 0\nif 1 < 2 { $x = 10 } else { $x = 20 }");
        Assert.False(state.IsError);
        Assert.Equal(10, GetInt("x"));
    }

    [Fact]
    public async Task If_FalseCondition_ExecutesElseBranch()
    {
        var state = await RunScriptAsync("$x = 0\nif 1 > 2 { $x = 10 } else { $x = 20 }");
        Assert.False(state.IsError);
        Assert.Equal(20, GetInt("x"));
    }

    [Fact]
    public async Task If_FalseCondition_NoElse_DoesNothing()
    {
        var state = await RunScriptAsync("$x = 5\nif false { $x = 99 }");
        Assert.False(state.IsError);
        Assert.Equal(5, GetInt("x"));
    }

    [Fact]
    public async Task While_Counts_UntilConditionFalse()
    {
        var state = await RunScriptAsync("$i = 0\nwhile $i < 3 { $i = ($i + 1) }");
        Assert.False(state.IsError);
        Assert.Equal(3, GetInt("i"));
    }

    [Fact]
    public async Task While_Break_ExitsEarly()
    {
        var state = await RunScriptAsync("$i = 0\nwhile $i < 100 { $i = ($i + 1)\nif $i == 5 break }");
        Assert.False(state.IsError);
        Assert.Equal(5, GetInt("i"));
    }

    [Fact]
    public async Task While_Continue_SkipsRemainderOfBody()
    {
        var script = "$i = 0\n$count = 0\nwhile $i < 5 { $i = ($i + 1)\nif $i == 3 continue\n$count = ($count + 1) }";
        var state = await RunScriptAsync(script);
        Assert.False(state.IsError);
        Assert.Equal(5, GetInt("i"));
        Assert.Equal(4, GetInt("count"));
    }

    [Fact]
    public async Task DoWhile_ExecutesBodyAtLeastOnce_WhenConditionFalse()
    {
        var state = await RunScriptAsync("$x = 0\ndo { $x = ($x + 1) } while $x < 0");
        Assert.False(state.IsError);
        Assert.Equal(1, GetInt("x"));
    }

    [Fact]
    public async Task For_OverArray_AccumulatesValues()
    {
        var state = await RunScriptAsync("$sum = 0\nfor $i in [1, 2, 3, 4] { $sum = ($sum + $i) }");
        Assert.False(state.IsError);
        Assert.Equal(10, GetInt("sum"));
    }

    [Fact]
    public async Task For_Break_StopsIteration()
    {
        var state = await RunScriptAsync("$sum = 0\nfor $i in [1, 2, 3, 4] { if $i == 3 break\n$sum = ($sum + $i) }");
        Assert.False(state.IsError);
        Assert.Equal(3, GetInt("sum"));
    }

    [Fact]
    public async Task For_Continue_SkipsSelectedIterations()
    {
        var state = await RunScriptAsync("$sum = 0\nfor $i in [1, 2, 3, 4] { if $i == 2 continue\n$sum = ($sum + $i) }");
        Assert.False(state.IsError);
        Assert.Equal(8, GetInt("sum"));
    }

    [Fact]
    public async Task Loop_Break_TerminatesWithCounter()
    {
        var state = await RunScriptAsync("$i = 0\nloop { $i = ($i + 1)\nif $i == 7 break }");
        Assert.False(state.IsError);
        Assert.Equal(7, GetInt("i"));
    }

    [Fact]
    public async Task NestedControlFlow_ComputesExpected()
    {
        var script = "$total = 0\nfor $i in [1, 2, 3] { $j = 0\nwhile $j < $i { $total = ($total + 1)\n$j = ($j + 1) } }";
        var state = await RunScriptAsync(script);
        Assert.False(state.IsError);
        Assert.Equal(6, GetInt("total"));
    }

    [Fact]
    public async Task Block_ExecutesStatementsSequentially()
    {
        var state = await RunScriptAsync("{ $a = 1\n$b = ($a + 1)\n$c = ($b + 1) }");
        Assert.False(state.IsError);
        Assert.Equal(1, GetInt("a"));
        Assert.Equal(2, GetInt("b"));
        Assert.Equal(3, GetInt("c"));
    }

    [Fact]
    public async Task Assignment_ChainedArithmetic_ComputesExpected()
    {
        var state = await RunScriptAsync("$x = ((2 + 3) * 4 - 1)");
        Assert.False(state.IsError);
        Assert.Equal(19, GetInt("x"));
    }

    [Fact]
    public async Task For_OverStrings_BindsTextElements()
    {
        var state = await RunScriptAsync("for $x in [\"a\", \"b\"] { }");
        Assert.False(state.IsError);
        Assert.Equal("b", Assert.IsType<ShellText>(GetVariable("x")).Text);
    }

    [Fact]
    public async Task For_OverBooleans_BindsBoolElements()
    {
        var state = await RunScriptAsync("for $x in [true, false] { }");
        Assert.False(state.IsError);
        Assert.False(Assert.IsType<ShellBool>(GetVariable("x")).Value);
    }

    [Fact]
    public async Task For_OverNull_BindsNullAsText()
    {
        var state = await RunScriptAsync("for $x in [null] { }");
        Assert.False(state.IsError);
        Assert.Equal("null", Assert.IsType<ShellText>(GetVariable("x")).Text);
    }

    [Fact]
    public async Task For_OverObjects_BindsJsonElements()
    {
        var state = await RunScriptAsync("for $x in [{ id: 1 }] { }");
        Assert.False(state.IsError);
        Assert.IsType<ShellJson>(GetVariable("x"));
    }

    [Fact]
    public async Task For_OverNestedArrays_BindsJsonElements()
    {
        var state = await RunScriptAsync("for $x in [[1, 2], [3, 4]] { }");
        Assert.False(state.IsError);
        Assert.IsType<ShellJson>(GetVariable("x"));
    }

    [Fact]
    public async Task For_OverNonArray_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunScriptAsync("for $x in 5 { }"));
    }
}
