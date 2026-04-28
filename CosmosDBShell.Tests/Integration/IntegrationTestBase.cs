// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Text.Json;
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
        return await Shell.ExecuteCommandAsync(script, TestContext.Current.CancellationToken);
    }

    internal static JsonElement GetJson(CommandState state)
    {
        return Assert.IsType<ShellJson>(state.Result).Value;
    }

    internal static string GetText(CommandState state)
    {
        return Assert.IsType<ShellText>(state.Result).Text;
    }

    internal static string GetErrorMessage(CommandState state)
    {
        return Assert.IsType<ErrorCommandState>(state).Exception.Message;
    }

    internal static string FormatError(CommandState state)
    {
        return state is ErrorCommandState err ? err.Exception.ToString() : "not an error";
    }

    internal static async Task<string> ReadRedirectAsync(string outputFile)
    {
        return await File.ReadAllTextAsync(outputFile, TestContext.Current.CancellationToken);
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

    /// <summary>
    /// Converts a file-system path to forward slashes so the shell lexer
    /// does not interpret backslashes as escape sequences inside double-quoted strings.
    /// </summary>
    internal static string ShellPath(string path) => path.Replace('\\', '/');

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
