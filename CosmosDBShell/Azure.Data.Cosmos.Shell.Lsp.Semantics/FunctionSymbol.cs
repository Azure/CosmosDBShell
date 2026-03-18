// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Azure.Data.Cosmos.Shell.Lsp.Semantics;

/// <summary>
/// Symbol representing a user-defined function (reserved for future expansion of the language).
/// </summary>
public sealed class FunctionSymbol(string name, int start, int length) : Symbol(name, SymbolKind.Function, start, length);