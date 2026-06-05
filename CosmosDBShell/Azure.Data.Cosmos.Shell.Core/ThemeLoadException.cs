// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

using System;

/// <summary>
/// Thrown when a theme file cannot be loaded (parse error, missing base, invalid value, …).
/// </summary>
internal sealed class ThemeLoadException : Exception
{
    public ThemeLoadException(string message)
        : base(message)
    {
    }

    public ThemeLoadException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
