// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

internal sealed record FilterIndexSegment(int Index, bool Optional) : FilterPathSegment(Optional);