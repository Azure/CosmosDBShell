// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using System;
using System.IO;
using System.Text;

using Azure.Data.Cosmos.Shell.Parser;

namespace CosmosShell.Fuzzer;

/// <summary>
/// Simple fuzzer that mutates input and tests the parser
/// </summary>
public class SimpleFuzzer
{
    private static readonly Random Random = new Random();
    public static void Run()
    {
        Console.WriteLine("Running simple fuzzer...");

        var testCases = Directory.GetFiles("testcases", "*.csh");
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
                    // Console.WriteLine(mutated);
                    TestParser(mutated);

                    if (i % 1000 == 0)
                    {
                        Console.Write(".");
                    }
                }
                catch (Exception ex) when (!(ex is StackOverflowException || ex is OutOfMemoryException))
                {
                    // Log interesting crashes
                    crashes++;
                    var crashFile = Path.Combine("findings", $"crash_{crashes:D4}.txt");
                    Directory.CreateDirectory("findings");
                    File.WriteAllText(crashFile, mutated);
                    File.AppendAllText(crashFile, $"\n\n// Exception: {ex.GetType().Name}\n// {ex.Message}");
                    File.AppendAllText(crashFile, $"\n\n// Trace: {ex.StackTrace}");
                    Console.WriteLine($"\nFound crash #{crashes}: {ex.GetType().Name}");
                }
            }
            Console.WriteLine();
        }

        Console.WriteLine($"Fuzzing complete. Found {crashes} crashes.");
    }

    private static string MutateInput(string input)
    {
        var strategy = Random.Next(10);

        return strategy switch
        {
            0 => FlipRandomBits(input),
            1 => InsertRandomCharacters(input),
            2 => DeleteRandomCharacters(input),
            3 => DuplicateRandomParts(input),
            4 => ShuffleLines(input),
            5 => InsertSpecialCharacters(input),
            6 => RepeatCharacters(input),
            7 => InsertNestedStructures(input),
            8 => InsertLargeNumbers(input),
            _ => SwapCharacters(input)
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
        var maxLength = Math.Min(10, input.Length - start);
        var length = maxLength > 1 ? Random.Next(1, maxLength) : 1;
        var part = input.Substring(start, length);
        return input.Insert(Random.Next(input.Length), part);
    }

    private static string ShuffleLines(string input)
    {
        var lines = input.Split('\n');
        for (int i = lines.Length - 1; i > 0; i--)
        {
            var j = Random.Next(i + 1);
            (lines[i], lines[j]) = (lines[j], lines[i]);
        }
        return string.Join('\n', lines);
    }

    private static string InsertSpecialCharacters(string input)
    {
        var specials = new[] { '\0', '\r', '\n', '\t', '"', '\'', '\\', '$', '`', '|', '>', '<', '&', ';' };
        var sb = new StringBuilder(input);
        sb.Insert(Random.Next(sb.Length + 1), specials[Random.Next(specials.Length)]);
        return sb.ToString();
    }

    private static string RepeatCharacters(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var pos = Random.Next(input.Length);
        var count = Random.Next(10, 100);
        return input.Insert(pos, new string(input[pos], count));
    }

    private static string InsertNestedStructures(string input)
    {
        var structures = new[] { "{{{{", "}}}}", "((((", "))))", "[[[[", "]]]]", "\"\"\"\"" };
        return input + structures[Random.Next(structures.Length)];
    }

    private static string InsertLargeNumbers(string input)
    {
        var largeNum = new string('9', Random.Next(100, 1000));
        return input.Insert(Random.Next(input.Length + 1), largeNum);
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

    private static void TestParser(string input)
    {
        var lexer = new Lexer(input);
        var parser = new StatementParser(lexer);
        var statements = parser.ParseStatements();

        foreach (var statement in statements)
        {
            _ = statement.ToString();
            _ = statement.Start;
            _ = statement.Length;
        }
    }
}