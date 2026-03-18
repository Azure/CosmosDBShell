// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Util;

/// <summary>
/// Provides functionality for pattern matching operations.
/// </summary>
public partial class PatternMatcher
{
    /// <summary>
    /// Represents the commands used in pattern matching.
    /// </summary>
    private enum Command
    {
        /// <summary>
        /// Command to match a specific character.
        /// </summary>
        Match,

        /// <summary>
        /// Command to match any single character.
        /// </summary>
        AnyChar,

        /// <summary>
        /// Command to match zero or more characters.
        /// </summary>
        ZeroOrMoreChars,
    }

    /// <summary>
    /// Represents an instruction in the pattern matching process.
    /// </summary>
    private class Instruction
    {
        /// <summary>
        /// Instruction to match any single character.
        /// </summary>
        public static readonly Instruction AnyChar = new(Command.AnyChar, '\0');

        /// <summary>
        /// Instruction to match zero or more characters.
        /// </summary>
        public static readonly Instruction ZeroOrMoreChars = new(Command.ZeroOrMoreChars, '\0');

        /// <summary>
        /// Initializes a new instance of the <see cref="Instruction"/> class.
        /// </summary>
        /// <param name="command">The command associated with the instruction.</param>
        /// <param name="ch">The character associated with the instruction.</param>
        public Instruction(Command command, char ch)
        {
            this.Command = command;
            this.Char = ch;
        }

        /// <summary>
        /// Gets or sets the command associated with the instruction.
        /// </summary>
        public Command Command { get; set; }

        /// <summary>
        /// Gets or sets the character associated with the instruction.
        /// </summary>
        public char Char { get; set; }
    }
}