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
}
