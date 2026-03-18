// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Provides functionality to match text against a pattern containing wildcards.
/// </summary>
public partial class PatternMatcher
{
    private readonly List<Instruction> compiledPattern = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PatternMatcher"/> class with the specified pattern.
    /// </summary>
    /// <param name="pattern">The pattern to compile for matching.</param>
    public PatternMatcher(string pattern)
    {
        foreach (char ch in pattern)
        {
            switch (ch)
            {
                case '?':
                    this.compiledPattern.Add(Instruction.AnyChar);
                    break;
                case '*':
                    this.compiledPattern.Add(Instruction.ZeroOrMoreChars);
                    break;
                default:
                    this.compiledPattern.Add(new Instruction(Command.Match, ch));
                    break;
            }
        }
    }

    /// <summary>
    /// Matches the specified text against the compiled pattern.
    /// </summary>
    /// <param name="text">The text to match.</param>
    /// <returns><c>true</c> if the text matches the pattern; otherwise, <c>false</c>.</returns>
    public bool Match(string text)
    {
        return this.Match(text, 0, 0);
    }

    /// <summary>
    /// Recursively matches the text against the compiled pattern.
    /// </summary>
    /// <param name="text">The text to match.</param>
    /// <param name="pc">The current position in the compiled pattern.</param>
    /// <param name="offset">The current position in the text.</param>
    /// <returns><c>true</c> if the text matches the pattern; otherwise, <c>false</c>.</returns>
    private bool Match(string text, int pc, int offset)
    {
        if (pc >= this.compiledPattern.Count && offset >= text.Length)
        {
            return true;
        }

        if (pc >= this.compiledPattern.Count)
        {
            return false;
        }

        var cur = this.compiledPattern[pc];
        if (offset >= text.Length)
        {
            return cur.Command == Command.ZeroOrMoreChars && this.compiledPattern.Count == pc + 1;
        }

        switch (cur.Command)
        {
            case Command.AnyChar:
                return this.Match(text, pc + 1, offset + 1);
            case Command.Match:
                if (text[offset] != cur.Char)
                {
                    return false;
                }

                return this.Match(text, pc + 1, offset + 1);
            case Command.ZeroOrMoreChars:
                return this.Match(text, pc + 1, offset) || this.Match(text, pc, offset + 1);
            default:
                throw new ApplicationException("Unknown command: " + cur.Command);
        }
    }
}