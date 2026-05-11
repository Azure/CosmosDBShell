// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Core;

using System.Collections.Generic;

/// <summary>
/// Result of a successful <see cref="ThemeFile.Load"/> or <see cref="ThemeFile.Parse"/>.
/// </summary>
internal sealed record ThemeFileResult(
    string Name,
    string? Description,
    string Extends,
    ThemeOptions Options,
    string Source,
    IReadOnlyList<string> Warnings);
