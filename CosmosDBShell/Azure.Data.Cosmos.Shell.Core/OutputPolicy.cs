// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Central policy for deciding how command output is presented. It distinguishes the
/// structured (machine-consumable) formats from the interactive (human-facing) ones and
/// resolves whether the shell should run in machine mode. This is the single source of
/// truth shared by startup option handling in <c>Program</c> and the runtime checks in
/// <see cref="ShellInterpreter"/>, so machine-mode detection is never re-derived ad hoc.
/// </summary>
internal static class OutputPolicy
{
    /// <summary>
    /// Determines whether the given format is a structured, machine-consumable format.
    /// <see cref="OutputFormat.JSon"/> and <see cref="OutputFormat.CSV"/> are structured
    /// (JSON for programmatic consumption, CSV for piping into a file); whereas
    /// <see cref="OutputFormat.User"/> and <see cref="OutputFormat.Table"/> are human-facing
    /// presentation formats.
    /// </summary>
    /// <param name="format">The effective output format.</param>
    /// <returns><see langword="true"/> if the format is structured; otherwise <see langword="false"/>.</returns>
    internal static bool IsStructuredFormat(OutputFormat format)
        => format is OutputFormat.JSon or OutputFormat.CSV;

    /// <summary>
    /// Determines whether the shell should run in machine mode, where banners, colors, and
    /// informational messages are suppressed in favor of deterministic structured output.
    /// Machine mode is entered by <c>--quiet</c>, by selecting a structured output format
    /// (<c>json</c> or <c>csv</c>), or by an execute-and-quit (<c>-c</c>) invocation that
    /// did not request a human-facing format.
    /// </summary>
    /// <param name="output">The raw <c>--output</c> token, if any.</param>
    /// <param name="quiet">Whether <c>--quiet</c> was supplied.</param>
    /// <param name="executeAndQuit">Whether an execute-and-quit (<c>-c</c>) command was supplied.</param>
    /// <returns><see langword="true"/> if the shell should run in machine mode; otherwise <see langword="false"/>.</returns>
    internal static bool IsMachineMode(string? output, bool quiet, bool executeAndQuit)
    {
        if (quiet)
        {
            return true;
        }

        if (OutputFormats.TryParse(output, out var format))
        {
            return IsStructuredFormat(format);
        }

        return executeAndQuit;
    }
}
