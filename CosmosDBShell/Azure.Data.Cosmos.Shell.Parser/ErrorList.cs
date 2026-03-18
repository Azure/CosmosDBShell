// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Represents a list of <see cref="ParseError"/> objects with error-checking functionality.
/// </summary>
public class ErrorList : List<ParseError>
{
    /// <summary>
    /// Gets a value indicating whether the list contains any errors (not just warnings).
    /// </summary>
    public bool HasErrors => this.Any(e => e.ErrorLevel == ErrorLevel.Error);
}