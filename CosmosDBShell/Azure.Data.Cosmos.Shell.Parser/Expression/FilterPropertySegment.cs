// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

internal sealed record FilterPropertySegment(string Name, bool Optional) : FilterPathSegment(Optional);