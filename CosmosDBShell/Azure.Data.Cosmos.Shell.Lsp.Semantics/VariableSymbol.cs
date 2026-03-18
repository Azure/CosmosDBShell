// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Lsp.Semantics;

/// <summary>
/// Symbol representing a variable. The first encountered occurrence is treated as its definition.
/// </summary>
public sealed class VariableSymbol(string name, int start, int length) : Symbol(name, SymbolKind.Variable, start, length);
