// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Fuzzer;

/// <summary>
/// Simple fuzzer that mutates input and tests the parser
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Cosmos Shell Fuzzer ===");
        Console.WriteLine();

        // Parse command line arguments
        bool runMcp = false;
        bool runSimple = false;
        bool runAll = false;

        if (args.Length == 0)
        {
            // Default to --all if no arguments
            runAll = true;
        }
        else
        {
            foreach (var arg in args)
            {
                switch (arg.ToLowerInvariant())
                {
                    case "--mcp":
                    case "-m":
                        runMcp = true;
                        break;
                    case "--simple":
                    case "-s":
                        runSimple = true;
                        break;
                    case "--all":
                    case "-a":
                        runAll = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        ShowHelp();
                        return;
                    default:
                        Console.WriteLine($"Unknown argument: {arg}");
                        ShowHelp();
                        return;
                }
            }
        }

        // If --all is specified, run everything
        if (runAll)
        {
            runMcp = true;
            runSimple = true;
        }

        // If nothing specific was selected, default to all
        if (!runMcp && !runSimple && !runAll)
        {
            runAll = true;
            runMcp = true;
            runSimple = true;
        }

        try
        {
            // Run MCP Fuzzer
            if (runMcp)
            {
                Console.WriteLine("=== Running MCP Protocol Fuzzer ===");
                Console.WriteLine();
                await McpFuzzer.RunAsync();
                Console.WriteLine();
            }

            // Run Simple Fuzzer
            if (runSimple)
            {
                Console.WriteLine("=== Running Simple Fuzzer ===");
                Console.WriteLine();
                SimpleFuzzer.Run();
                Console.WriteLine();
            }

            Console.WriteLine("=== Fuzzing Complete ===");
            Console.WriteLine($"Results saved to: {Path.GetFullPath("findings")}");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"\nFuzzer encountered an error: {ex.Message}");
            if (args.Contains("--verbose") || args.Contains("-v"))
            {
                await Console.Error.WriteLineAsync($"Stack trace:\n{ex.StackTrace}");
            }
            Environment.Exit(1);
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Cosmos Shell Fuzzer - Test robustness with malformed inputs");
        Console.WriteLine();
        Console.WriteLine("Usage: CosmosDBShell.Fuzzer [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --mcp, -m      Run MCP (Model Context Protocol) JSON-RPC fuzzing");
        Console.WriteLine("  --simple, -s   Run simple general-purpose fuzzing tests");
        Console.WriteLine("  --all, -a      Run all fuzzers (default if no options specified)");
        Console.WriteLine("  --help, -h     Show this help message");
        Console.WriteLine("  --verbose, -v  Show detailed error information");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  CosmosDBShell.Fuzzer              # Run all fuzzers");
        Console.WriteLine("  CosmosDBShell.Fuzzer --mcp        # Run only MCP fuzzer");
        Console.WriteLine("  CosmosDBShell.Fuzzer --simple     # Run only simple fuzzer");
        Console.WriteLine("  CosmosDBShell.Fuzzer --mcp --simple # Run both fuzzers");
    }
}