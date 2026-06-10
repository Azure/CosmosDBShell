// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Text.Json;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;

namespace CosmosShell.Fuzzer;

/// <summary>
/// Fuzzer that mutates import seed files (JSON Lines, JSON array, CSV) and drives the
/// import command's parsing helpers. These helpers consume untrusted file content, so they
/// must never crash on malformed input: the only exceptions they are permitted to raise are
/// <see cref="CommandException"/> and <see cref="JsonException"/>. Anything else is logged
/// to the findings directory as a real fault.
/// </summary>
internal static class ImportFuzzer
{
    private static readonly Random Random = new Random();
    private static readonly char[] Separators = { ',', ';', '\t', '|' };

    public static async Task RunAsync()
    {
        Console.WriteLine("=== Import Parser Fuzzer ===");
        Console.WriteLine("Mutating JSONL/array/CSV seeds against import parsing helpers\n");

        if (!Directory.Exists("importtestcases"))
        {
            Console.WriteLine("No 'importtestcases' directory found; skipping import fuzzing.");
            return;
        }

        var testCases = Directory.GetFiles("importtestcases");
        var iterations = 10000;
        var crashes = 0;

        foreach (var testFile in testCases)
        {
            var original = File.ReadAllText(testFile);
            Console.WriteLine($"Fuzzing based on: {Path.GetFileName(testFile)}");

            for (int i = 0; i < iterations; i++)
            {
                var mutated = MutateInput(original);

                try
                {
                    await DriveParsersAsync(mutated);

                    if (i % 1000 == 0)
                    {
                        Console.Write(".");
                    }
                }
                catch (Exception ex) when (IsExpected(ex))
                {
                    // Expected, well-defined rejection of malformed input.
                }
                catch (Exception ex) when (!(ex is StackOverflowException || ex is OutOfMemoryException))
                {
                    crashes++;
                    Directory.CreateDirectory("findings");
                    var crashFile = Path.Combine("findings", $"import_crash_{crashes:D4}.txt");
                    File.WriteAllText(crashFile, mutated);
                    File.AppendAllText(crashFile, $"\n\n// Exception: {ex.GetType().FullName}\n// {ex.Message}");
                    File.AppendAllText(crashFile, $"\n\n// Trace: {ex.StackTrace}");
                    Console.WriteLine($"\nFound import crash #{crashes}: {ex.GetType().Name}");
                }
            }

            Console.WriteLine();
        }

        Console.WriteLine($"Import fuzzing complete. Found {crashes} crashes.");
    }

    private static bool IsExpected(Exception ex)
    {
        return ex is CommandException || ex is JsonException;
    }

    /// <summary>
    /// Routes the mutated content through every import parsing helper so a single mutation
    /// exercises format detection, both JSON readers, and the CSV parser/object builder.
    /// </summary>
    private static async Task DriveParsersAsync(string content)
    {
        // Format detection must never throw, regardless of input.
        var detected = ImportCommand.DetectFormat(content);
        _ = detected;

        // JSON Lines path.
        using (var reader = new StringReader(content))
        {
            await ConsumeJsonLinesAsync(reader);
        }

        // JSON array path.
        var bytes = Encoding.UTF8.GetBytes(content);
        using (var stream = new MemoryStream(bytes))
        {
            await ConsumeArrayAsync(stream);
        }

        // CSV path, exercised with several separators and partition-key shapes.
        var separator = Separators[Random.Next(Separators.Length)];
        var records = ImportCommand.ParseCsv(content, separator);
        if (records.Count > 0)
        {
            var headers = records[0];
            var pk = ImportCommand.ParsePartitionKeySegments(RandomPartitionKey());
            for (var r = 1; r < records.Count; r++)
            {
                _ = ImportCommand.BuildCsvObject(headers, records[r], pk);
            }
        }
    }

    private static async Task ConsumeJsonLinesAsync(TextReader reader)
    {
        await foreach (var (_, item) in ImportCommand.EnumerateJsonLinesAsync(reader, CancellationToken.None))
        {
            _ = item.ValueKind;
        }
    }

    private static async Task ConsumeArrayAsync(Stream stream)
    {
        await foreach (var (_, item) in ImportCommand.EnumerateArrayAsync(stream, CancellationToken.None))
        {
            _ = item.ValueKind;
        }
    }

    private static string? RandomPartitionKey()
    {
        return Random.Next(4) switch
        {
            0 => null,
            1 => "/city",
            2 => "/address/city",
            _ => "/a/b/c",
        };
    }

    private static string MutateInput(string input)
    {
        var strategy = Random.Next(11);

        return strategy switch
        {
            0 => FlipRandomBits(input),
            1 => InsertRandomCharacters(input),
            2 => DeleteRandomCharacters(input),
            3 => DuplicateRandomParts(input),
            4 => TruncateInput(input),
            5 => InsertSpecialCharacters(input),
            6 => UnbalanceQuotes(input),
            7 => InsertNestedStructures(input),
            8 => InsertLargeNumbers(input),
            9 => InsertControlBytes(input),
            _ => SwapCharacters(input),
        };
    }

    private static string FlipRandomBits(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var bytes = Encoding.UTF8.GetBytes(input);
        var pos = Random.Next(bytes.Length);
        bytes[pos] ^= (byte)(1 << Random.Next(8));
        return Encoding.UTF8.GetString(bytes);
    }

    private static string InsertRandomCharacters(string input)
    {
        var sb = new StringBuilder(input);
        var count = Random.Next(1, 5);
        for (int i = 0; i < count; i++)
        {
            var pos = Random.Next(sb.Length + 1);
            sb.Insert(pos, (char)Random.Next(32, 127));
        }
        return sb.ToString();
    }

    private static string DeleteRandomCharacters(string input)
    {
        if (input.Length < 2) return input;
        var sb = new StringBuilder(input);
        var count = Math.Min(Random.Next(1, 5), sb.Length);
        for (int i = 0; i < count && sb.Length > 0; i++)
        {
            sb.Remove(Random.Next(sb.Length), 1);
        }
        return sb.ToString();
    }

    private static string DuplicateRandomParts(string input)
    {
        if (input.Length < 2) return input;
        var start = Random.Next(input.Length);
        var maxLength = Math.Min(16, input.Length - start);
        var length = maxLength > 1 ? Random.Next(1, maxLength) : 1;
        var part = input.Substring(start, length);
        return input.Insert(Random.Next(input.Length), part);
    }

    private static string TruncateInput(string input)
    {
        if (input.Length < 2) return input;
        return input.Substring(0, Random.Next(1, input.Length));
    }

    private static string InsertSpecialCharacters(string input)
    {
        var specials = new[] { '\0', '\r', '\n', '\t', '"', '\'', '\\', ',', ';', '{', '}', '[', ']', ':', '\uFEFF' };
        var sb = new StringBuilder(input);
        sb.Insert(Random.Next(sb.Length + 1), specials[Random.Next(specials.Length)]);
        return sb.ToString();
    }

    private static string UnbalanceQuotes(string input)
    {
        var sb = new StringBuilder(input);
        var count = Random.Next(1, 4);
        for (int i = 0; i < count; i++)
        {
            sb.Insert(Random.Next(sb.Length + 1), '"');
        }
        return sb.ToString();
    }

    private static string InsertNestedStructures(string input)
    {
        var structures = new[] { "{{{{", "}}}}", "[[[[", "]]]]", "\"\"\"\"", "{\"a\":", ",,,,", "::::" };
        return input + structures[Random.Next(structures.Length)];
    }

    private static string InsertLargeNumbers(string input)
    {
        var largeNum = new string('9', Random.Next(100, 1000));
        return input.Insert(Random.Next(input.Length + 1), largeNum);
    }

    private static string InsertControlBytes(string input)
    {
        var sb = new StringBuilder(input);
        var count = Random.Next(1, 5);
        for (int i = 0; i < count; i++)
        {
            sb.Insert(Random.Next(sb.Length + 1), (char)Random.Next(0, 32));
        }
        return sb.ToString();
    }

    private static string SwapCharacters(string input)
    {
        if (input.Length < 2) return input;
        var chars = input.ToCharArray();
        var i = Random.Next(chars.Length);
        var j = Random.Next(chars.Length);
        (chars[i], chars[j]) = (chars[j], chars[i]);
        return new string(chars);
    }
}
