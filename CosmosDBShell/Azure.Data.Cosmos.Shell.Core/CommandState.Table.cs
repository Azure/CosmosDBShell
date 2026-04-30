// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Azure.Data.Cosmos.Shell.Core;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

public partial class CommandState
{
    internal static string EscapeCSV(object obj)
    {
        var str = obj?.ToString() ?? string.Empty;
        return '"' + str.Replace("\"", "\"\"") + '"';
    }

    private static Table ResultToTable(IList<JsonElement> results)
    {
        var headers = new Dictionary<string, int>();
        var columns = new List<List<string?>>();
        var row = 0;
        foreach (var e in results)
        {
            var curColumn = 0;
            foreach (var prop in e.EnumerateObject())
            {
                if (!headers.TryGetValue(prop.Name, out var index))
                {
                    index = curColumn;
                    var list = new List<string?>
                    {
                        EscapeCSV(prop.Name),
                    };
                    for (int i = 0; i < row; i++)
                    {
                        list.Add(null);
                    }

                    foreach (var kv in headers)
                    {
                        if (kv.Value >= curColumn)
                        {
                            headers[kv.Key] = kv.Value + 1;
                        }
                    }

                    headers[prop.Name] = curColumn;
                    if (columns.Count <= curColumn)
                    {
                        columns.Add(list);
                    }
                    else
                    {
                        columns.Insert(curColumn, list);
                    }
                }

                var column = columns[index];

                // fill up missing items (if any)
                while (column.Count < row)
                {
                    column.Add(null);
                }

                column.Add(EscapeCSV(prop.Value.ToString()));
                curColumn += 1;
            }

            row += 1;
        }

        // ensure all columns have the same length
        foreach (var column in columns)
        {
            while (column.Count <= row)
            {
                column.Add(null);
            }
        }

        return new Table(columns);
    }

    private class Table
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Table"/> class with the specified columns.
        /// </summary>
        /// <param name="columns">
        /// A list of columns, where each column is represented as a list of nullable strings.
        /// The first column's row count must match the row count of all other columns.
        /// </param>
        /// <exception cref="System.Diagnostics.Debug.AssertException">
        /// Thrown if any column does not have the same number of rows as the first column (in debug builds).
        /// </exception>
        public Table(List<List<string?>> columns)
        {
            for (int i = 1; i < columns.Count; i++)
            {
                Debug.Assert(columns[0].Count == columns[i].Count, $"Column 0 count ({columns[0].Count}) does not match column {i} count ({columns[i].Count})");
            }

            this.Cols = columns;
        }

        public int Rows => this.Cols.Count == 0 ? 0 : this.Cols[0].Count;

        public int Columns => this.Cols.Count;

        private List<List<string?>> Cols { get; }

        public string? this[int i, int j]
        {
            get
            {
                if (j >= this.Cols.Count)
                {
                    throw new IndexOutOfRangeException($"Invalid colum: {j} valid: 0..{this.Cols.Count}");
                }

                if (i >= this.Cols[j].Count)
                {
                    throw new IndexOutOfRangeException($"Invalid row: {i} valid: 0..{this.Cols[j].Count}");
                }

                return this.Cols[j][i];
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            var rows = this.Rows;
            var cols = this.Columns;
            var sep = ShellInterpreter.CSVSeparator;
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (j > 0)
                    {
                        sb.Append(sep);
                    }

                    if (this[i, j] != null)
                    {
                        sb.Append(this[i, j]);
                    }
                    else
                    {
                        sb.Append("\"\"");
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        public string ToGridString()
        {
            var rows = this.Rows;
            var cols = this.Columns;
            if (rows == 0 || cols == 0)
            {
                return string.Empty;
            }

            var widths = new int[cols];
            for (int j = 0; j < cols; j++)
            {
                for (int i = 0; i < rows; i++)
                {
                    var cell = UnescapeCSV(this[i, j]);
                    if (cell.Length > widths[j])
                    {
                        widths[j] = cell.Length;
                    }
                }
            }

            var sb = new StringBuilder();
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (j > 0)
                    {
                        sb.Append("  ");
                    }

                    var cell = UnescapeCSV(this[i, j]);
                    sb.Append(cell.PadRight(widths[j]));
                }

                sb.AppendLine();

                if (i == 0 && rows > 1)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        if (j > 0)
                        {
                            sb.Append("  ");
                        }

                        sb.Append(new string('-', widths[j]));
                    }

                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static string UnescapeCSV(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                return value[1..^1].Replace("\"\"", "\"");
            }

            return value;
        }
    }
}
