// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Parser;

internal static class PositionalErrorHelper
{
    internal static (int Line, int Column, string LineText) GetLineAndColumn(string text, int position)
    {
        var line = 1;
        var column = 1;
        var lineStart = 0;

        for (var i = 0; i < position && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
                lineStart = i + 1;
            }
            else
            {
                column++;
            }
        }

        var lineEnd = text.IndexOf('\n', lineStart);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var lineText = text[lineStart..lineEnd].TrimEnd('\r');
        return (line, column, lineText);
    }
}
