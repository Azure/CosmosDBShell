//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

internal readonly struct ColorizeRule
{
    public ColorizeRule(string field, string value, string style)
    {
        this.Field = field;
        this.Value = value;
        this.Style = style;
    }

    public string Field { get; }

    public string Value { get; }

    public string Style { get; }
}
