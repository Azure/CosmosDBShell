// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Pre-formatted source line and caret pieces for a single diagnostic.
/// <see cref="CaretLeader"/> + <see cref="CaretPad"/> + <see cref="CaretMarker"/>
/// together form the caret row, indented under the gutter by the caller.
/// <see cref="CaretLeader"/> repeats the leading ellipsis glyph of
/// <see cref="Display"/> verbatim so the caret stays aligned even when a
/// terminal renders the ellipsis wider than one cell.
/// </summary>
internal readonly record struct RenderedSourceCaret(
    string Display,
    int CaretColumn,
    int SourceColumn,
    string CaretLeader,
    string CaretPad,
    string CaretMarker);
