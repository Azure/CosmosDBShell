//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.KeyBindings;

internal readonly record struct ReverseHistorySearchResult(int Index, int Skip, string Match)
{
    public bool HasMatch => this.Index >= 0;

    public static ReverseHistorySearchResult None(int skip = 0) => new(-1, skip, string.Empty);
}
