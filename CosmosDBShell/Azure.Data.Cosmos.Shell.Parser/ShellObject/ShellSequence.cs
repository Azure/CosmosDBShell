// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Linq;
using System.Text.Json;

internal class ShellSequence : ShellObject
{
    public ShellSequence(IEnumerable<JsonElement> elements)
        : base(DataType.Json)
    {
        this.Elements = elements.Select(static e => e.Clone()).ToList();
    }

    public IReadOnlyList<JsonElement> Elements { get; }

    public override object ConvertShellObject(DataType type)
    {
        var array = FilterExpressionUtilities.ToJsonArray(this.Elements);
        return type switch
        {
            DataType.Json => array,
            DataType.Text => array.GetRawText(),
            _ => new ShellJson(array).ConvertShellObject(type),
        };
    }
}