// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class UserDefinedFunctionTests : IntegrationTestBase
{
    [Fact]
    public async Task Def_SimpleFunction_CanBeInvoked()
    {
        var outputFile = CaptureOutputFile();
        var state = await RunScriptAsync("def greet [who] { echo $\"Hello $who\" }\ngreet \"World\"");

        Assert.False(state.IsError);
        var text = await File.ReadAllTextAsync(outputFile);
        Assert.Contains("Hello World", text);
    }

    [Fact]
    public async Task Def_FunctionWithReturn_ReturnsValue()
    {
        var state = await RunScriptAsync("def greet [name] { return $\"Hi $name\" }\n$result = (greet \"Alice\")");

        Assert.False(state.IsError);
        var result = GetVariable("result");
        Assert.NotNull(result);
        Assert.Equal("Hi Alice", result.ConvertShellObject(Azure.Data.Cosmos.Shell.Parser.DataType.Text)?.ToString());
    }

    [Fact]
    public async Task Def_FunctionScopeIsolation_NoVariableLeak()
    {
        var state = await RunScriptAsync("def inner [] { $secret = 42 }\ninner");

        Assert.False(state.IsError);
        Assert.Throws<ShellException>(() => GetVariable("secret"));
    }

    [Fact]
    public async Task Def_NestedFunctionCalls()
    {
        var state = await RunScriptAsync("def add_suffix [x] { return ($x + \"!\") }\ndef greet [name] { $suffixed = (add_suffix $name)\nreturn $suffixed }\n$result = (greet \"hi\")");

        Assert.False(state.IsError);
        var result = GetVariable("result");
        Assert.NotNull(result);
        Assert.Equal("hi!", result.ConvertShellObject(Azure.Data.Cosmos.Shell.Parser.DataType.Text)?.ToString());
    }
}
