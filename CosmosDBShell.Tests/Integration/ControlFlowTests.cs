// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class ControlFlowTests : IntegrationTestBase
{
    [Fact]
    public async Task IfElse_TrueBranch_Executes()
    {
        var state = await RunScriptAsync("if true { $x = 1 } else { $x = 2 }");

        Assert.False(state.IsError);
        var x = GetVariable("x");
        var n = Assert.IsType<ShellNumber>(x);
        Assert.Equal(1, n.Value);
    }

    [Fact]
    public async Task IfElse_FalseBranch_Executes()
    {
        var state = await RunScriptAsync("if false { $x = 1 } else { $x = 2 }");

        Assert.False(state.IsError);
        var x = GetVariable("x");
        var n = Assert.IsType<ShellNumber>(x);
        Assert.Equal(2, n.Value);
    }

    [Fact]
    public async Task While_Loop_CountsCorrectly()
    {
        var state = await RunScriptAsync("$i = 0\nwhile ($i < 5) { $i = ($i + 1) }");

        Assert.False(state.IsError);
        var i = GetVariable("i");
        var n = Assert.IsType<ShellNumber>(i);
        Assert.Equal(5, n.Value);
    }

    [Fact]
    public async Task Loop_WithBreak_Terminates()
    {
        var state = await RunScriptAsync("$i = 0\nloop { $i = ($i + 1)\nif ($i == 3) { break } }");

        Assert.False(state.IsError);
        var i = GetVariable("i");
        var n = Assert.IsType<ShellNumber>(i);
        Assert.Equal(3, n.Value);
    }

    [Fact]
    public async Task ForIn_WithBreak_StopsEarly()
    {
        var state = await RunScriptAsync("$sum = 0\nfor $x in [1, 2, 3, 4, 5] {\nif ($x == 4) { break }\n$sum = ($sum + $x) }");

        Assert.False(state.IsError);
        var sum = GetVariable("sum");
        var n = Assert.IsType<ShellNumber>(sum);
        // 1 + 2 + 3 = 6 (breaks before processing 4)
        Assert.Equal(6, n.Value);
    }

    [Fact]
    public async Task DoWhile_ExecutesAtLeastOnce()
    {
        var state = await RunScriptAsync("$i = 0\ndo { $i = ($i + 1) } while ($i < 0)");

        Assert.False(state.IsError);
        var i = GetVariable("i");
        var n = Assert.IsType<ShellNumber>(i);
        Assert.Equal(1, n.Value);
    }
}
