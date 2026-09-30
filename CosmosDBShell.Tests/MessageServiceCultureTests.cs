// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests;

using System.Globalization;
using Azure.Data.Cosmos.Shell.Util;
using Fluent.Net;

public class MessageServiceCultureTests
{
    [Fact]
    public void ApplicationAssembly_ContainsEnglishFallbackResource()
    {
        Assert.Contains(
            typeof(MessageService).Assembly.GetManifestResourceNames(),
            name => name.EndsWith("lang.en.ftl", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("pt-BR", "pt-BR", "pt")]
    [InlineData("zh-Hans-CN", "zh-Hans-CN", "zh-Hans", "zh")]
    [InlineData("zh-Hant", "zh-Hant", "zh")]
    [InlineData("de-DE", "de-DE", "de")]
    [InlineData("en-US", "en-US")]
    public void GetCultureFallbacks_ReturnsSpecificToNeutralCultures(string cultureName, params string[] expected)
    {
        Assert.Equal(expected, MessageService.GetCultureFallbacks(CultureInfo.GetCultureInfo(cultureName)));
    }

    [Fact]
    public void CreateMessageContext_FallsBackToInvariantCulture_WhenLocaleIsNotSupported()
    {
        var options = new MessageContextOptions { UseIsolating = false };
        var attempts = new List<string[]>();

        var context = MessageService.CreateMessageContext(
            "en",
            options,
            locales =>
            {
                var localeList = locales.ToArray();
                attempts.Add(localeList);
                if (localeList.SequenceEqual(["en"]))
                {
                    throw new CultureNotFoundException("name", "en", "unsupported");
                }

                return new MessageContext(localeList, options);
            });

        Assert.Equal(
            [
                ["en"],
                [CultureInfo.InvariantCulture.Name, "en"],
            ],
            attempts);
        Assert.NotNull(context);
    }
}