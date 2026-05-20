// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Implemented by exceptions that carry a short, already-localized hint
/// sentence ("Did you mean 'X'?", "Try connecting first.") that the
/// shell renders on a separate, non-error-colored line below the
/// primary error message.
/// </summary>
internal interface IShellExceptionWithHint
{
    /// <summary>
    /// Gets the formatted hint text to show on a second line, or null
    /// when there is no hint to surface.
    /// </summary>
    string? Hint { get; }
}
