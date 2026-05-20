// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Pre-formatted source line and caret pieces for a single diagnostic.
/// <see cref="CaretPad"/> + <see cref="CaretMarker"/> together form the caret
/// row, indented under the gutter by the caller.
/// </summary>
internal readonly record struct RenderedSourceCaret(
    string Display,
    int CaretColumn,
    int SourceColumn,
    string CaretPad,
    string CaretMarker);
