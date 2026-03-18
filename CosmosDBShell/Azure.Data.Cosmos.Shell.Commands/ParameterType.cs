//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Commands;

/// <summary>
/// Specifies the type of parameter used in a command.
/// </summary>
[Flags]
public enum ParameterType
{
    /// <summary>
    /// Unknown parameter type.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// File parameter type.
    /// </summary>
    File = 1,

    /// <summary>
    /// Database parameter type.
    /// </summary>
    Database = 2,

    /// <summary>
    /// Container parameter type.
    /// </summary>
    Container = 4,
}
