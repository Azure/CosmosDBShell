// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Collections.Generic;

/// <summary>
/// A format-agnostic table a command can hand back so that both the CSV and the
/// Table output formats render from the same source. When a <see cref="CommandState"/>
/// supplies one, it takes precedence over the default JSON-derived tabulation, letting
/// commands control column headers (for example "Container" instead of "value").
/// </summary>
internal sealed class TabularData
{
    private readonly List<IReadOnlyList<string>> rows = new();

    public TabularData(params string[] headers)
    {
        this.Headers = headers;
    }

    public IReadOnlyList<string> Headers { get; }

    public IReadOnlyList<IReadOnlyList<string>> Rows => this.rows;

    public TabularData AddRow(params string[] cells)
    {
        this.rows.Add(cells);
        return this;
    }
}
