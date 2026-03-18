// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Lsp.Semantics;

/// <summary>
/// Symbol representing a command invocation target (only tracked if unknown or for reference features).
/// </summary>
public sealed class CommandSymbol(string name, int start, int length) : Symbol(name, SymbolKind.Command, start, length);
