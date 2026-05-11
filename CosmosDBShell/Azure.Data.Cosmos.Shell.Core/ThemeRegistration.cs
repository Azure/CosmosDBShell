// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>One row in the merged theme registry.</summary>
internal sealed record ThemeRegistration(
    string Name,
    ThemeOptions Options,
    ThemeSource Source,
    string? Path,
    string? Description)
{
    /// <summary>The base profile name, when this entry came from a file. Otherwise <c>null</c>.</summary>
    public string? Extends { get; init; }
}
