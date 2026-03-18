// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

internal class PositionalException : Exception
{
    public PositionalException(string fileName, Exception innerException, int line, int column, string? lineText = null)
      : base(innerException?.Message, innerException)
    {
        this.FileName = fileName;
        this.Line = line;
        this.Column = column;
        this.LineText = lineText;
    }

    /// <summary>
    /// Gets the name of the file where the exception occurred.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the line number where the exception occurred (1-based).
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Gets the column number where the exception occurred (1-based).
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// Gets the text of the line where the exception occurred.
    /// </summary>
    public string? LineText { get; }
}
