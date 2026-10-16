// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

internal sealed class CommandWarning(string message)
{
    public string Message { get; set; } = message;

    public bool IsRendered { get; set; }

    public string? LastRenderedMessage { get; set; }

    public string? RenderedErrorRedirect { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether machine mode deferred this warning while stderr
    /// was the console, so it is written there even if a later statement redirects stderr.
    /// </summary>
    public bool DeferredToConsole { get; set; }

    public bool EmittedAsMachineModeWarningRecord { get; set; }
}
