// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// One-based source-location result from <see cref="QueryErrorLocator"/>.
/// </summary>
internal sealed record QueryErrorLocation(int Line, int Column, int Length, string? Message);
