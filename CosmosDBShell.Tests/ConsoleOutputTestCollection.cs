// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleOutputTestCollection
{
    public const string Name = "Console output tests";
}