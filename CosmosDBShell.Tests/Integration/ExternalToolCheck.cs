// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using System.Diagnostics;

using Xunit.Sdk;

internal static class ExternalToolCheck
{
    internal static void SkipIfMissing(string tool)
    {
        if (!IsToolAvailable(tool))
        {
            throw SkipException.ForSkip($"External tool '{tool}' is not installed.");
        }
    }

    private static bool IsToolAvailable(string tool)
    {
        try
        {
            var psi = new ProcessStartInfo(tool, "--version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
