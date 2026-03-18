// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.ArgumentParser;

using System.Text.Json;

internal abstract class JsonOperation
{
    public abstract JsonElement Evaluate(JsonElement element);
}
