// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Shell;

// Tests in this collection either mutate the global Theme.Current (via
// Theme.Apply(...)) or assert on highlighted markup whose output depends on
// the active theme. Running them in parallel can cause a transient Monochrome
// theme to be observed by a highlighter test, producing markup with no style
// tags and a single collapsed segment.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ThemeStateTestCollection
{
    public const string Name = "Theme state tests";
}
