// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public abstract class IntegrationTestBase : IDisposable
{
    private readonly List<string> tempFiles = [];

    internal ShellInterpreter Shell { get; }

    protected IntegrationTestBase()
    {
        Shell = ShellInterpreter.CreateInstance();
    }

    internal async Task<CommandState> RunScriptAsync(string script)
    {
        return await Shell.ExecuteCommandAsync(script, CancellationToken.None);
    }

    internal string CaptureOutputFile()
    {
        var outputFile = Path.Combine(Path.GetTempPath(), $"inttest-{Guid.NewGuid():N}.txt");
        tempFiles.Add(outputFile);
        Shell.StdOutRedirect = outputFile;
        return outputFile;
    }

    internal ShellObject? GetVariable(string name)
    {
        return Shell.GetVariable(name);
    }

    internal void SetVariable(string name, ShellObject value)
    {
        Shell.SetVariable(name, value);
    }

    public virtual void Dispose()
    {
        foreach (var file in tempFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }

        Shell.Dispose();
        GC.SuppressFinalize(this);
    }
}
