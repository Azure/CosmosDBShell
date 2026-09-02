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
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  localization export <source.ftl> <catalog.json>");
        Console.Error.WriteLine("  localization import <source.ftl> <catalog.json> <output.ftl>");
        Console.Error.WriteLine("  localization verify <source.ftl> <catalog.json>");
    }
}