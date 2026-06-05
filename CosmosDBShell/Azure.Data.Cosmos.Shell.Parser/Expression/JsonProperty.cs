// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Represents a single parsed property of a <see cref="JsonExpression"/>, retaining the exact
/// token positions of the key, the colon separator, and the trailing comma (when present) so
/// that consumers such as syntax highlighters can rely on real spans instead of re-scanning
/// the source text.
/// </summary>
internal sealed class JsonProperty
{
    public JsonProperty(Token keyToken, Token? colonToken, Expression value)
    {
        this.KeyToken = keyToken ?? throw new ArgumentNullException(nameof(keyToken));
        this.ColonToken = colonToken;
        this.Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets the token for the property name (quoted string, identifier, or number).</summary>
    public Token KeyToken { get; }

    /// <summary>Gets the colon token separating the key and value, or <see langword="null"/> if absent.</summary>
    public Token? ColonToken { get; }

    /// <summary>Gets the comma token following the property, or <see langword="null"/> for the last property.</summary>
    public Token? CommaToken { get; set; }

    /// <summary>Gets the value expression for the property.</summary>
    public Expression Value { get; }
}
