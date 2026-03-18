// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public abstract class TestBase : IDisposable
{
    internal ShellInterpreter Shell { get; }

    protected TestBase()
    {
        Shell = new ShellInterpreter();
    }

    internal async Task<CommandState> RunScriptAsync(string script)
    {
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();
        var state = new CommandState();

        foreach (var statement in statements)
        {
            state = await statement.RunAsync(Shell, state, CancellationToken.None);
            if (state.IsError)
            {
                break;
            }
        }

        return state;
    }

    internal ShellObject? GetVariable(string name)
    {
        return Shell.GetVariable(name);
    }

    internal void SetVariable(string name, ShellObject value)
    {
        Shell.SetVariable(name, value);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}