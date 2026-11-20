// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Tracks whether warnings were written to one error-redirect destination, so later output
/// to the same destination appends instead of truncating it.
/// </summary>
internal sealed class ErrorRedirectWarningScope
{
    public bool HasWarning { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether shell output (warnings, errors, or required
    /// instructions) was written to this destination, so a later machine-mode error appends.
    /// </summary>
    public bool HasOutput { get; set; }

    public bool HasContent => this.HasWarning || this.HasOutput;
}
