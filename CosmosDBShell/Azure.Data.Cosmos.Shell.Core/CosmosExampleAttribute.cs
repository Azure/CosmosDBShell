// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Specifies example usage for a Cosmos Shell command.
/// This attribute can be applied multiple times to provide multiple examples.
/// </summary>
/// <example>
/// <code>
/// [CosmosCommand("query")]
/// [CosmosExample("query \"SELECT * FROM c\"", Description = "Query all documents")]
/// [CosmosExample("query \"SELECT * FROM c WHERE c.id = 'test'\"", Description = "Filter by ID")]
/// internal class QueryCommand : CosmosCommand
/// {
///     // Command implementation
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal class CosmosExampleAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosExampleAttribute"/> class.
    /// </summary>
    /// <param name="example">The example command usage to display in help documentation.</param>
    public CosmosExampleAttribute(string example)
    {
        this.Example = example;
    }

    /// <summary>
    /// Gets the example command usage string.
    /// </summary>
    /// <value>
    /// A string representing an example of how to use the command.
    /// </value>
    public string Example { get; }

    /// <summary>
    /// Gets or sets the description of what this example demonstrates.
    /// </summary>
    /// <value>
    /// A brief description explaining the purpose or scenario of this example.
    /// </value>
    public string? Description { get; set; }
}