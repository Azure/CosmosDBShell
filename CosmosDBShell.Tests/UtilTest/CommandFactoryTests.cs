// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Util;

[CosmosCommand("simple")]
internal class SimpleCommand : CosmosCommand
{
    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        throw new NotImplementedException();
    }
}


[CosmosCommand("test_arguments")]
internal class ArgumentsCommand : CosmosCommand
{
    [CosmosParameter("arg1")]
    public string? Arg1 { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        throw new System.NotImplementedException();
    }
}

[CosmosCommand("test_arguments")]
internal class ArgsCommand : CosmosCommand
{
    [CosmosParameter("args", IsRequired = false)]
    public string[]? Args { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        throw new System.NotImplementedException();
    }
}


[CosmosCommand("test_bool")]
internal class BoolCommand : CosmosCommand
{
    [CosmosOption("a")]
    public bool? A { get; init; }

    [CosmosOption("b")]
    public bool? B { get; init; }

    public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
    {
        throw new System.NotImplementedException();
    }
}

public class CommandFactoryTests
{
    [Fact]
    public void TestFail()
    {
        Assert.False(CommandFactory.TryCreateFactory(typeof(CommandFactoryTests), out var factory));
    }

    [Fact]
    public void TestSimpleFactoryLookup()
    {
        Assert.True(CommandFactory.TryCreateFactory(typeof(SimpleCommand), out var factory));
        Assert.Equal("simple", factory.CommandName);
        Assert.Empty(factory.Parameters);
    }


}
