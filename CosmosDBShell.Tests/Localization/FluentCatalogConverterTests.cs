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

        using var exportReader = new StringReader(source);
        var catalog = FluentCatalogConverter.Export(exportReader);
        catalog["greeting"] = "{0}, hello!";
        catalog["removed"] = "{1}: {0}.";
        catalog["removed.__p1.one"] = "entry";
        catalog["removed.__p1.other"] = "entries";

        using var importReader = new StringReader(source);
        var translated = FluentCatalogConverter.Import(importReader, catalog);
        var context = new MessageContext("en", new MessageContextOptions { UseIsolating = false });
        using var translatedReader = new StringReader(translated);
        Assert.Empty(context.AddMessages(translatedReader));
        Assert.Equal("Ada, hello!", context.Format(context.GetMessage("greeting"), new Dictionary<string, object> { ["name"] = "Ada" }));
        Assert.Equal("entry: 1.", context.Format(context.GetMessage("removed"), new Dictionary<string, object> { ["count"] = 1 }));
        Assert.Equal("entries: 2.", context.Format(context.GetMessage("removed"), new Dictionary<string, object> { ["count"] = 2 }));
    }

    [Fact]
    public void Import_RejectsMissingPlaceholder()
    {
        const string source = "greeting = Hello, { $name }!";
        using var exportReader = new StringReader(source);
        var catalog = FluentCatalogConverter.Export(exportReader);
        catalog["greeting"] = "Hello!";

        using var importReader = new StringReader(source);
        var exception = Assert.Throws<InvalidDataException>(() => FluentCatalogConverter.Import(importReader, catalog));

        Assert.Contains("missing placeholder", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_RejectsOversizedPlaceholderIndex()
    {
        const string source = "greeting = Hello, { $name }!";
        using var exportReader = new StringReader(source);
        var catalog = FluentCatalogConverter.Export(exportReader);
        catalog["greeting"] = "Hello, {999999999999}!";

        using var importReader = new StringReader(source);
        var exception = Assert.Throws<InvalidDataException>(() => FluentCatalogConverter.Import(importReader, catalog));

        Assert.Contains("invalid placeholder", exception.Message, StringComparison.OrdinalIgnoreCase);
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
        using var exportReader = new StringReader(source);
        var catalog = FluentCatalogConverter.Export(exportReader);
        catalog["items.__p0.other"] = "many items\non several lines";

        using var importReader = new StringReader(source);
        var translated = FluentCatalogConverter.Import(importReader, catalog);
        var context = new MessageContext("en", new MessageContextOptions { UseIsolating = false });

        using var translatedReader = new StringReader(translated);
        Assert.Empty(context.AddMessages(translatedReader));
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

        using var exportReader = new StringReader(source);
        var catalog = FluentCatalogConverter.Export(exportReader);
        catalog["items.__p0.0"] = "none";
        catalog["items.__p0.1"] = "single";

        using var importReader = new StringReader(source);
        var translated = FluentCatalogConverter.Import(importReader, catalog);
        var context = new MessageContext("en", new MessageContextOptions { UseIsolating = false });

        using var translatedReader = new StringReader(translated);
        Assert.Empty(context.AddMessages(translatedReader));
        Assert.Equal("none", context.Format(context.GetMessage("items"), new Dictionary<string, object> { ["count"] = 0 }));
        Assert.Equal("single", context.Format(context.GetMessage("items"), new Dictionary<string, object> { ["count"] = 1 }));
    }

    [Fact]
    public void Import_AllowsRepeatedPlaceholders()
    {
        const string source = "greeting = Hello, { $name }!";
        using var exportReader = new StringReader(source);
        var catalog = FluentCatalogConverter.Export(exportReader);
        catalog["greeting"] = "{0}, meet {0}.";

        using var importReader = new StringReader(source);
        var translated = FluentCatalogConverter.Import(importReader, catalog);
        var context = new MessageContext("en", new MessageContextOptions { UseIsolating = false });
        using var translatedReader = new StringReader(translated);

        Assert.Empty(context.AddMessages(translatedReader));
        Assert.Equal("Ada, meet Ada.", context.Format(context.GetMessage("greeting"), new Dictionary<string, object> { ["name"] = "Ada" }));
    }

    [Fact]
    public void EnglishCatalog_RoundTripsAllPatterns()
    {
        var root = FindRepositoryRoot();
        var sourcePath = Path.Join(root, "CosmosDBShell", "lang", "en.ftl");
        var sourceText = File.ReadAllText(sourcePath, Encoding.UTF8);
        using var exportReader = new StringReader(sourceText);
        var catalog = FluentCatalogConverter.Export(exportReader);

        using var importReader = new StringReader(sourceText);
        var generated = FluentCatalogConverter.Import(importReader, catalog);
        using var roundTripReader = new StringReader(generated);
        var roundTrippedCatalog = FluentCatalogConverter.Export(roundTripReader);

        Assert.True(catalog.Count > 1000);
        Assert.Equal(catalog, roundTrippedCatalog);
    }

    [Fact]
    public void VerifyFile_IgnoresJsonPropertyOrdering()
    {
        var directory = Path.Join(Path.GetTempPath(), $"cosmos-l10n-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Join(directory, "en.ftl");
            var catalogPath = Path.Join(directory, "catalog.json");
            File.WriteAllText(sourcePath, "z_key = Last\na-key = First");
            File.WriteAllText(catalogPath, "{\"z_key\":\"Last\",\"a-key\":\"First\"}");

            FluentCatalogConverter.VerifyFile(sourcePath, catalogPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExportFile_CreatesAndUpdatesCatalog()
    {
        var directory = Path.Join(Path.GetTempPath(), $"cosmos-l10n-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Join(directory, "en.ftl");
            var catalogPath = Path.Join(directory, "l10n", "catalog.json");
            File.WriteAllText(sourcePath, "greeting = Hello!");

            FluentCatalogConverter.ExportFile(sourcePath, catalogPath);
            FluentCatalogConverter.VerifyFile(sourcePath, catalogPath);

            File.WriteAllText(sourcePath, "greeting = Welcome!");
            FluentCatalogConverter.ExportFile(sourcePath, catalogPath);
            FluentCatalogConverter.VerifyFile(sourcePath, catalogPath);
            Assert.Contains("Welcome!", File.ReadAllText(catalogPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ExportFile_PreservesUnchangedCatalog(string newline)
    {
        var directory = Path.Join(Path.GetTempPath(), $"cosmos-l10n-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Join(directory, "en.ftl");
            var catalogPath = Path.Join(directory, "catalog.json");
            File.WriteAllText(sourcePath, "greeting = Hello!");
            FluentCatalogConverter.ExportFile(sourcePath, catalogPath);
            var content = File.ReadAllText(catalogPath).ReplaceLineEndings(newline);
            File.WriteAllText(catalogPath, content);
            File.SetLastWriteTimeUtc(catalogPath, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var timestamp = File.GetLastWriteTimeUtc(catalogPath);

            FluentCatalogConverter.ExportFile(sourcePath, catalogPath);

            Assert.Equal(content, File.ReadAllText(catalogPath));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(catalogPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void VerifyFile_RejectsStaleCatalogWithoutChangingIt()
    {
        var directory = Path.Join(Path.GetTempPath(), $"cosmos-l10n-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Join(directory, "en.ftl");
            var catalogPath = Path.Join(directory, "catalog.json");
            File.WriteAllText(sourcePath, "greeting = Hello!");
            FluentCatalogConverter.ExportFile(sourcePath, catalogPath);
            var content = File.ReadAllText(catalogPath);
            var timestamp = File.GetLastWriteTimeUtc(catalogPath);
            File.WriteAllText(sourcePath, "greeting = Welcome!");

            Assert.Throws<InvalidDataException>(() => FluentCatalogConverter.VerifyFile(sourcePath, catalogPath));
            Assert.Equal(content, File.ReadAllText(catalogPath));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(catalogPath));
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
            if (File.Exists(Path.Join(directory.FullName, "CosmosDBShell.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}