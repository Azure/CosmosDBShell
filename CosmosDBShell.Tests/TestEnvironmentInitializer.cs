// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Runtime.CompilerServices;

internal static class TestEnvironmentInitializer
{
    // Shells created by tests persist command history; keep it out of the developer's real config directory.
    [ModuleInitializer]
    internal static void IsolateShellConfigDirectory()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COSMOSDB_SHELL_CONFIG_DIR")))
        {
            Environment.SetEnvironmentVariable(
                "COSMOSDB_SHELL_CONFIG_DIR",
                Path.Join(Path.GetTempPath(), $"cosmosshell-tests-{Environment.ProcessId}"));
        }
    }
}
