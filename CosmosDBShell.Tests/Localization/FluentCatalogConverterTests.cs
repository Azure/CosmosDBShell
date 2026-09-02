// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Localization;

using System.Text;
using CosmosDBShell.Localization;
using Fluent.Net;

public class FluentCatalogConverterTests
{
    [Fact]
    public void ExportAndImport_PreservesReorderedPlaceholdersAndSelectVariants()
    {
        const string source = """
            greeting = Hello, { $name }!
            removed = Removed { $count } { $count ->
                [one] item
               *[other] items
            }.
            """;

        var catalog = FluentCatalogConverter.Export(new StringReader(source));
        catalog["greeting"] = "{0}, hello!";
        catalog["removed"] = "{1}: {0}.";
        catalog["removed.__p1.one"] = "entry";
        catalog["removed.__p1.other"] = "entries";

        var translated = FluentCatalogConverter.Import(new StringReader(source), catalog);
        var context = new MessageContext("en", new MessageContextOptions { UseIsolating = false });
        Assert.Empty(context.AddMessages(new StringReader(translated)));
        Assert.Equal("Ada, hello!", context.Format(context.GetMessage("greeting"), new Dictionary<string, object> { ["name"] = "Ada" }));
        Assert.Equal("entry: 1.", context.Format(context.GetMessage("removed"), new Dictionary<string, object> { ["count"] = 1 }));
        Assert.Equal("entries: 2.", context.Format(context.GetMessage("removed"), new Dictionary<string, object> { ["count"] = 2 }));
    }

    [Fact]
    public void Import_RejectsMissingPlaceholder()
    {
        const string source = "greeting = Hello, { $name }!";
        var catalog = FluentCatalogConverter.Export(new StringReader(source));
        catalog["greeting"] = "Hello!";

        var exception = Assert.Throws<InvalidDataException>(() => FluentCatalogConverter.Import(new StringReader(source), catalog));

        Assert.Contains("missing placeholder", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_SupportsMultilineTranslatedSelectVariant()
    {
        const string source = """
            items = { $count ->
                [one] one item
               *[other] many items
            }
            """;
        var catalog = FluentCatalogConverter.Export(new StringReader(source));
        catalog["items.__p0.other"] = "many items\non several lines";

        var translated = FluentCatalogConverter.Import(new StringReader(source), catalog);
        var context = new MessageContext("en", new MessageContextOptions { UseIsolating = false });

        Assert.Empty(context.AddMessages(new StringReader(translated)));
        Assert.Equal(
            "many items\non several lines",
            context.Format(context.GetMessage("items"), new Dictionary<string, object> { ["count"] = 2 }));
    }

    [Fact]
    public void ExportAndImport_PreservesNumericSelectVariants()
    {
        const string source = """
            items = { $count ->
                [0] no items
                [1] one item
               *[other] many items
            }
            """;

        var catalog = FluentCatalogConverter.Export(new StringReader(source));
        catalog["items.__p0.0"] = "none";
        catalog["items.__p0.1"] = "single";

        var translated = FluentCatalogConverter.Import(new StringReader(source), catalog);
        var context = new MessageContext("en", new MessageContextOptions { UseIsolating = false });

        Assert.Empty(context.AddMessages(new StringReader(translated)));
        Assert.Equal("none", context.Format(context.GetMessage("items"), new Dictionary<string, object> { ["count"] = 0 }));
        Assert.Equal("single", context.Format(context.GetMessage("items"), new Dictionary<string, object> { ["count"] = 1 }));
    }

    [Fact]
    public void EnglishCatalog_RoundTripsAllPatterns()
    {
        var root = FindRepositoryRoot();
        var sourcePath = Path.Combine(root, "CosmosDBShell", "lang", "en.ftl");
        var sourceText = File.ReadAllText(sourcePath, Encoding.UTF8);
        var catalog = FluentCatalogConverter.Export(new StringReader(sourceText));

        var generated = FluentCatalogConverter.Import(new StringReader(sourceText), catalog);
        var roundTrippedCatalog = FluentCatalogConverter.Export(new StringReader(generated));

        Assert.True(catalog.Count > 1000);
        Assert.Equal(catalog, roundTrippedCatalog);
    }

    [Fact]
    public void VerifyFile_IgnoresJsonPropertyOrdering()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cosmos-l10n-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "en.ftl");
            var catalogPath = Path.Combine(directory, "catalog.json");
            File.WriteAllText(sourcePath, "z_key = Last\na-key = First");
            File.WriteAllText(catalogPath, "{\"z_key\":\"Last\",\"a-key\":\"First\"}");

            FluentCatalogConverter.VerifyFile(sourcePath, catalogPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CosmosDBShell.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}