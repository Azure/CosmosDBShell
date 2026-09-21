// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosDBShell.Localization;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                WriteUsage();
                return 2;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "export" when args.Length == 3:
                    FluentCatalogConverter.ExportFile(args[1], args[2]);
                    break;
                case "import" when args.Length == 4:
                    FluentCatalogConverter.ImportFile(args[1], args[2], args[3]);
                    break;
                case "verify" when args.Length == 3:
                    FluentCatalogConverter.VerifyFile(args[1], args[2]);
                    break;
                default:
                    WriteUsage();
                    return 2;
            }

            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (System.Text.Json.JsonException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (FormatException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  dotnet run --project tools/Localization/CosmosDBShell.Localization.csproj -- export <source.ftl> <catalog.json>");
        Console.Error.WriteLine("  dotnet run --project tools/Localization/CosmosDBShell.Localization.csproj -- import <source.ftl> <catalog.json> <output.ftl>");
        Console.Error.WriteLine("  dotnet run --project tools/Localization/CosmosDBShell.Localization.csproj -- verify <source.ftl> <catalog.json>");
    }
}