// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

/// <summary>
/// Selects a specific Entra ID credential for <see cref="ShellInterpreter.ConnectAsync"/>.
/// Only credentials that are chosen purely by a flag belong here; credentials that
/// carry additional data (such as a managed-identity client id or an interactive
/// tenant/login hint) continue to be driven by their own parameters.
/// </summary>
internal enum CredentialMethod
{
    /// <summary>
    /// No explicit credential was requested. The connect flow falls through to its
    /// normal precedence, ending at <c>DefaultAzureCredential</c>.
    /// </summary>
    Default,

    /// <summary>
    /// Use the Visual Studio Code credential (typically when launched from the
    /// VS Code extension).
    /// </summary>
    VSCode,

    /// <summary>
    /// Use the signed-in Azure CLI (<c>az login</c>) identity, bypassing managed
    /// identity. Useful in environments such as Azure Cloud Shell where the managed
    /// identity may lack Cosmos DB data-plane access.
    /// </summary>
    AzureCli,
}
