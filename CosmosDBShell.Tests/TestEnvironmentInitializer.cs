// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Runtime.CompilerServices;

internal static class TestEnvironmentInitializer
{
    // Shells created by tests persist command history; always override, since a pre-set value may be the developer's real config.
    [ModuleInitializer]
    internal static void IsolateShellConfigDirectory()
    {
        Environment.SetEnvironmentVariable(
            "COSMOSDB_SHELL_CONFIG_DIR",
            Path.Join(Path.GetTempPath(), $"cosmosshell-tests-{Guid.NewGuid():N}"));
    }
}
