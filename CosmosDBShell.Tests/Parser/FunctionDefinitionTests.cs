// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CosmosShell.Tests.Parser;

public class FunctionDefinitionTests : TestBase
{
    [Fact]
    public async Task Arguments_PreserveNumericTypes()
    {
        var state = await RunScriptAsync("def add [a b] { return $a + $b }; $result = (add 2 3)");
        Assert.False(state.IsError);
        Assert.Equal(5, Assert.IsType<Azure.Data.Cosmos.Shell.Parser.ShellNumber>(GetVariable("result")).Value);
    }

    [Theory]
    [InlineData("identity 1")]
    [InlineData("identity true")]
    [InlineData("identity [1,2]")]
    [InlineData("identity {id: 1}")]
    public async Task Identity_PreservesArgumentType(string invocation)
    {
        var state = await RunScriptAsync($"def identity [value] {{ return $value }}; $result = ({invocation})");
        Assert.False(state.IsError);
        Assert.IsNotType<Azure.Data.Cosmos.Shell.Parser.ShellText>(GetVariable("result"));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("identity 1 2")]
    public async Task WrongArgumentCount_IsRejected(string invocation)
    {
        await RunScriptAsync("$value = 99; def identity [value] { return $value }");
        await Assert.ThrowsAsync<Azure.Data.Cosmos.Shell.Core.CommandException>(() => RunScriptAsync(invocation));
    }

    [Fact]
    public async Task Assignment_IsLocalToFunction()
    {
        var state = await RunScriptAsync("$value = 1; def change { $value = 2; return $value }; $local = (change)");
        Assert.False(state.IsError);
        Assert.Equal(1, Assert.IsType<Azure.Data.Cosmos.Shell.Parser.ShellNumber>(GetVariable("value")).Value);
        Assert.Equal(2, Assert.IsType<Azure.Data.Cosmos.Shell.Parser.ShellNumber>(GetVariable("local")).Value);
    }

    [Fact]
    public async Task Exception_RestoresCallerScope()
    {
        await RunScriptAsync("$value = 1; def fail { $value = 2; $invalid = 1 / 0 }");
        await Assert.ThrowsAsync<DivideByZeroException>(() => RunScriptAsync("fail"));
        Assert.Equal(1, Assert.IsType<Azure.Data.Cosmos.Shell.Parser.ShellNumber>(GetVariable("value")).Value);
        Assert.Single(Shell.VariableContainers);
    }

    [Fact]
    public async Task NestedCalls_ReadNearestFrame_WithoutChangingCaller()
    {
        var state = await RunScriptAsync("$value = 1; def inner { return $value }; def outer [value] { return (inner) }; $result = (outer 9)");
        Assert.False(state.IsError);
        Assert.Equal(9, Assert.IsType<Azure.Data.Cosmos.Shell.Parser.ShellNumber>(GetVariable("result")).Value);
        Assert.Equal(1, Assert.IsType<Azure.Data.Cosmos.Shell.Parser.ShellNumber>(GetVariable("value")).Value);
        Assert.Single(Shell.VariableContainers);
    }

}
