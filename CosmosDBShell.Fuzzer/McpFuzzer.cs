// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Mcp;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using System;
using System.Text;
using System.Text.Json;

namespace CosmosShell.Fuzzer;

internal static class McpFuzzer
{
    private static readonly Random Rnd = new();

    public static async Task RunAsync()
    {
        Console.WriteLine("=== MCP Protocol Fuzzer ===");
        Console.WriteLine("Testing JSON-RPC message mutations for edge cases\n");

        var seedsDir = Path.Combine("mcptestcases");
        Directory.CreateDirectory("findings");

        // Test JSON mutations
        var stats = await TestJsonMutationsAsync(seedsDir);

        // Print summary
        PrintSummary(stats);
    }


    private static async Task<FuzzingStats> TestJsonMutationsAsync(string seedsDir)
    {
        Console.WriteLine("=== JSON Mutation Testing ===\n");

        var stats = new FuzzingStats();
        var testCases = Directory.GetFiles(seedsDir, "*.json");
        var strategyStats = new Dictionary<int, (int valid, int invalid)>();

        foreach (var testCase in testCases)
        {
            var original = File.ReadAllText(testCase);
            var fileName = Path.GetFileName(testCase);

            Console.WriteLine($"Testing: {fileName}");
            var validation = ValidateJsonRpc(original);
            Console.WriteLine($"  Original: {validation.IsValid} - {validation.Reason}");

            // Generate mutations with different strategies
            for (int strategy = 0; strategy < 10; strategy++)
            {
                if (!strategyStats.ContainsKey(strategy))
                    strategyStats[strategy] = (0, 0);

                for (int i = 0; i < 20; i++)
                {
                    var mutated = MutateJsonWithStrategy(original, strategy);
                    stats.TotalMutations++;

                    var mutValidation = ValidateJsonRpc(mutated);
                    if (mutValidation.IsValid)
                    {
                        stats.ValidJson++;
                        strategyStats[strategy] = (strategyStats[strategy].valid + 1, strategyStats[strategy].invalid);
                    }
                    else
                    {
                        continue;
                        /*
                        stats.InvalidJson++;
                        strategyStats[strategy] = (strategyStats[strategy].valid, strategyStats[strategy].invalid + 1);

                        // Track error types
                        if (!stats.ErrorTypes.ContainsKey(mutValidation.Reason))
                            stats.ErrorTypes[mutValidation.Reason] = 0;
                        stats.ErrorTypes[mutValidation.Reason]++;

                        // Save interesting invalid cases
                        if (IsInterestingInvalid(mutated, mutValidation.Reason))
                        {
                            stats.InterestingCases++;
                            if (stats.InterestingCases <= 30)
                            {
                                var sanitizedFileName = fileName.Replace(".json", "");
                                var path = Path.Combine("findings", $"interesting_{stats.InterestingCases:D4}_{GetStrategyName(strategy).Replace(" ", "_")}_{sanitizedFileName}.json");
                                File.WriteAllText(path, mutated);
                                File.AppendAllText(path, $"\n// Original: {fileName}");
                                File.AppendAllText(path, $"\n// Strategy: {GetStrategyName(strategy)}");
                                File.AppendAllText(path, $"\n// Reason: {mutValidation.Reason}");
                                File.AppendAllText(path, $"\n// Length: {mutated.Length} bytes");
                            }
                        }*/
                    }

                    // Check for potential security issues
                    CheckSecurityIssues(mutated, stats);
                }
                Console.Write(".");
            }
            Console.WriteLine();
        }

        // Add strategy effectiveness to stats
        stats.StrategyEffectiveness = strategyStats.ToDictionary(
            kvp => GetStrategyName(kvp.Key),
            kvp => (double)kvp.Value.invalid / (kvp.Value.valid + kvp.Value.invalid) * 100
        );

        await Task.CompletedTask;
        return stats;
    }

    private static void CheckSecurityIssues(string json, FuzzingStats stats)
    {
        // Check for potential injection attacks
        if (json.Contains("../") || json.Contains("..\\"))
            stats.SecurityIssues.Add("Path traversal attempt");

        if (json.Contains("<script") || json.Contains("javascript:"))
            stats.SecurityIssues.Add("XSS attempt");

        if (json.Contains("'; DROP") || json.Contains("\"; DROP"))
            stats.SecurityIssues.Add("SQL injection attempt");

        // Check for command injection patterns
        if (json.Contains("$(") || json.Contains("`;") || json.Contains("&&") || json.Contains("||"))
            stats.SecurityIssues.Add("Command injection pattern");

        // Check for oversized inputs
        if (json.Length > 100000)
            stats.SecurityIssues.Add($"Oversized input: {json.Length} bytes");

        // Check for deep nesting (potential stack overflow)
        int nestingLevel = 0;
        int maxNesting = 0;
        foreach (char c in json)
        {
            if (c == '{' || c == '[') nestingLevel++;
            else if (c == '}' || c == ']') nestingLevel--;
            maxNesting = Math.Max(maxNesting, nestingLevel);
        }
        if (maxNesting > 50)
            stats.SecurityIssues.Add($"Deep nesting detected: {maxNesting} levels");
    }

    private static string GetStrategyName(int strategy) => strategy switch
    {
        0 => "Structured Mutation",
        1 => "Character Insertion",
        2 => "Character Removal",
        3 => "Duplicate Parts",
        4 => "Null Replacement",
        5 => "Special Characters",
        6 => "Truncation",
        7 => "Number Mutation",
        8 => "String Mutation",
        _ => "Bit Flipping"
    };

    private static string MutateJsonWithStrategy(string json, int strategy) => strategy switch
    {
        0 => MutateStructured(json),
        1 => InsertRandomCharacters(json),
        2 => RemoveRandomCharacters(json),
        3 => DuplicateRandomPart(json),
        4 => ReplaceWithNull(json),
        5 => InjectSpecialCharacters(json),
        6 => TruncateJson(json),
        7 => MutateNumbers(json),
        8 => MutateStrings(json),
        _ => FlipRandomBits(json)
    };

    private static void PrintSummary(FuzzingStats stats)
    {
        Console.WriteLine("\n=== Fuzzing Summary ===");
        Console.WriteLine($"Total Mutations: {stats.TotalMutations:N0}");
        Console.WriteLine($"Valid JSON-RPC: {stats.ValidJson:N0} ({100.0 * stats.ValidJson / stats.TotalMutations:F1}%)");
        Console.WriteLine($"Invalid JSON-RPC: {stats.InvalidJson:N0} ({100.0 * stats.InvalidJson / stats.TotalMutations:F1}%)");
        Console.WriteLine($"Interesting Cases: {stats.InterestingCases}");

        if (stats.StrategyEffectiveness.Any())
        {
            Console.WriteLine("\nStrategy Effectiveness (% causing errors):");
            foreach (var strategy in stats.StrategyEffectiveness.OrderByDescending(s => s.Value))
            {
                Console.WriteLine($"  {strategy.Key}: {strategy.Value:F1}%");
            }
        }

        if (stats.ErrorTypes.Any())
        {
            Console.WriteLine("\nTop Error Types:");
            foreach (var error in stats.ErrorTypes.OrderByDescending(e => e.Value).Take(5))
            {
                var shortError = error.Key.Length > 80 ? error.Key.Substring(0, 77) + "..." : error.Key;
                Console.WriteLine($"  {shortError}: {error.Value}");
            }
        }

        if (stats.SecurityIssues.Any())
        {
            Console.WriteLine("\nPotential Security Issues Detected:");
            var groupedIssues = stats.SecurityIssues.GroupBy(i => i).OrderByDescending(g => g.Count());
            foreach (var issue in groupedIssues)
            {
                Console.WriteLine($"  - {issue.Key} (found {issue.Count()} times)");
            }
        }

        Console.WriteLine($"\nFindings saved to: {Path.GetFullPath("findings")}");

        // Generate a summary report file
        var reportPath = Path.Combine("findings", "fuzzing_report.txt");
        GenerateReport(reportPath, stats);
        Console.WriteLine($"Full report saved to: {Path.GetFullPath(reportPath)}");
    }

    private static void GenerateReport(string path, FuzzingStats stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MCP Protocol Fuzzing Report");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();
        sb.AppendLine($"Total Mutations: {stats.TotalMutations:N0}");
        sb.AppendLine($"Valid JSON-RPC: {stats.ValidJson:N0} ({100.0 * stats.ValidJson / stats.TotalMutations:F1}%)");
        sb.AppendLine($"Invalid JSON-RPC: {stats.InvalidJson:N0} ({100.0 * stats.InvalidJson / stats.TotalMutations:F1}%)");
        sb.AppendLine($"Interesting Cases Found: {stats.InterestingCases}");
        sb.AppendLine();

        if (stats.ErrorTypes.Any())
        {
            sb.AppendLine("All Error Types:");
            foreach (var error in stats.ErrorTypes.OrderByDescending(e => e.Value))
            {
                sb.AppendLine($"  {error.Value,5}x - {error.Key}");
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static bool IsInterestingInvalid(string json, string reason)
    {
        // Consider cases interesting if they're not just basic syntax errors
        return !reason.Contains("Invalid JSON") &&
               !reason.Contains("Unexpected error") &&
               json.Length > 10 &&
               json.Length < 10000;
    }

    private static (bool IsValid, string Reason) ValidateJsonRpc(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0)
                    return (false, "Empty batch");

                // Validate each element in batch
                foreach (var element in root.EnumerateArray())
                {
                    var elementValidation = ValidateSingleJsonRpc(element);
                    if (!elementValidation.IsValid)
                        return elementValidation;
                }

                return (true, "Valid batch");
            }

            return ValidateSingleJsonRpc(root);
        }
        catch (JsonException ex)
        {
            var msg = ex.Message.Length > 100 ? ex.Message.Substring(0, 100) : ex.Message;
            return (false, $"Invalid JSON: {msg}");
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.GetType().Name}");
        }
    }

    private static (bool IsValid, string Reason) ValidateSingleJsonRpc(JsonElement root)
    {
        if (!root.TryGetProperty("jsonrpc", out var jsonrpc))
            return (false, "Missing 'jsonrpc' field");

        if (jsonrpc.ValueKind != JsonValueKind.String)
            return (false, "Invalid 'jsonrpc' type");

        if (jsonrpc.GetString() != "2.0")
            return (false, $"Invalid version: {jsonrpc.GetString()}");

        bool hasId = root.TryGetProperty("id", out var id);
        bool hasMethod = root.TryGetProperty("method", out var method);
        bool hasResult = root.TryGetProperty("result", out _);
        bool hasError = root.TryGetProperty("error", out _);

        // Validate ID type if present
        if (hasId)
        {
            if (id.ValueKind != JsonValueKind.String &&
                id.ValueKind != JsonValueKind.Number &&
                id.ValueKind != JsonValueKind.Null)
            {
                return (false, $"Invalid 'id' type: {id.ValueKind}");
            }
        }

        // Validate method if present
        if (hasMethod)
        {
            if (method.ValueKind != JsonValueKind.String)
                return (false, "Invalid 'method' type");

            if (string.IsNullOrEmpty(method.GetString()))
                return (false, "Empty 'method' field");
        }

        if (!hasMethod && !hasResult && !hasError)
            return (false, "Missing method/result/error");

        if (hasResult && hasError)
            return (false, "Both 'result' and 'error' present");

        return (true, hasMethod ? (hasId ? "Valid request" : "Valid notification") : "Valid response");
    }

    private static string MutateJson(string json)
    {
        var strategy = Rnd.Next(10);
        return MutateJsonWithStrategy(json, strategy);
    }

    private static string MutateStructured(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                // Handle array mutations
                var sb = new StringBuilder("[");
                int i = 0;
                foreach (var element in root.EnumerateArray())
                {
                    if (Rnd.NextDouble() < 0.9) // 10% chance to skip
                    {
                        if (i++ > 0) sb.Append(',');
                        sb.Append(element.GetRawText());
                    }
                }
                sb.Append(']');
                return sb.ToString();
            }

            var sb2 = new StringBuilder("{");
            int j = 0;

            foreach (var prop in root.EnumerateObject())
            {
                if (Rnd.NextDouble() < 0.1) continue;
                if (j++ > 0) sb2.Append(',');

                var name = prop.Name;
                if (Rnd.NextDouble() < 0.1)
                    name += "_mut" + Rnd.Next(100);

                sb2.Append('"').Append(name).Append("\":");

                if (Rnd.NextDouble() < 0.2)
                {
                    var type = Rnd.Next(4);
                    switch (type)
                    {
                        case 0: sb2.Append("null"); break;
                        case 1: sb2.Append(Rnd.Next(-1000, 1000)); break;
                        case 2: sb2.Append(Rnd.NextDouble() < 0.5 ? "true" : "false"); break;
                        default: sb2.Append('"').Append(RandomString(Rnd.Next(1, 20))).Append('"'); break;
                    }
                }
                else
                {
                    sb2.Append(prop.Value.GetRawText());
                }
            }

            sb2.Append('}');
            return sb2.ToString();
        }
        catch
        {
            return json;
        }
    }

    private static string RemoveRandomCharacters(string input) =>
        input.Length < 2 ? input : input.Remove(Rnd.Next(input.Length), 1);

    private static string InsertRandomCharacters(string input) =>
        input.Insert(Rnd.Next(input.Length + 1), ((char)Rnd.Next(32, 127)).ToString());

    private static string DuplicateRandomPart(string input)
    {
        if (input.Length < 10) return input;
        var start = Rnd.Next(input.Length - 5);
        var len = Math.Min(Rnd.Next(5, 20), input.Length - start);
        return input.Insert(Rnd.Next(input.Length), input.Substring(start, len));
    }

    private static string ReplaceWithNull(string input) =>
        System.Text.RegularExpressions.Regex.Replace(input, @"""[^""]+""", m => Rnd.NextDouble() < 0.1 ? "null" : m.Value);

    private static string InjectSpecialCharacters(string input)
    {
        var specials = new[] { "\0", "\r\n", "\t", "\\\"", "\\", "\u0001", "\u001F" };
        var pos = Rnd.Next(input.Length);
        return input.Insert(pos, specials[Rnd.Next(specials.Length)]);
    }

    private static string TruncateJson(string input)
    {
        if (input.Length < 10) return input;
        return input.Substring(0, Rnd.Next(10, Math.Min(input.Length, 100)));
    }

    private static string MutateNumbers(string input) =>
        System.Text.RegularExpressions.Regex.Replace(input, @"\d+", m =>
        {
            if (Rnd.NextDouble() < 0.3)
            {
                return Rnd.Next(5) switch
                {
                    0 => int.MaxValue.ToString(),
                    1 => int.MinValue.ToString(),
                    2 => "0",
                    3 => "-0",
                    _ => Rnd.Next(-999999, 999999).ToString()
                };
            }
            return m.Value;
        });

    private static string MutateStrings(string input) =>
        System.Text.RegularExpressions.Regex.Replace(input, @"""([^""]*)""", m =>
        {
            if (Rnd.NextDouble() < 0.2)
            {
                return Rnd.Next(5) switch
                {
                    0 => "\"\"", // Empty string
                    1 => "\"" + new string('A', 1000) + "\"", // Long string
                    2 => "\"\\u0000\"", // Null character
                    3 => "\"<script>alert('xss')</script>\"", // XSS attempt
                    _ => $"\"{RandomString(Rnd.Next(1, 30))}\""
                };
            }
            return m.Value;
        });

    private static string FlipRandomBits(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        if (bytes.Length > 0)
        {
            var numFlips = Rnd.Next(1, 4);
            for (int i = 0; i < numFlips; i++)
            {
                bytes[Rnd.Next(bytes.Length)] ^= (byte)(1 << Rnd.Next(8));
            }
        }
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return input; }
    }

    private static string RandomString(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.!@#$%^&*()<>[]{}|\\/'\"";
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
            sb.Append(chars[Rnd.Next(chars.Length)]);
        return sb.ToString();
    }

    private class FuzzingStats
    {
        public int TotalMutations { get; set; }
        public int ValidJson { get; set; }
        public int InvalidJson { get; set; }
        public int InterestingCases { get; set; }
        public Dictionary<string, int> ErrorTypes { get; } = new();
        public List<string> SecurityIssues { get; } = new();
        public Dictionary<string, double> StrategyEffectiveness { get; set; } = new();
    }
}