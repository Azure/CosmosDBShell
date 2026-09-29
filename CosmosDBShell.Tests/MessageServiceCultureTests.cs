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
        var attempts = new List<string>();

        var context = MessageService.CreateMessageContext(
            "en",
            options,
            locale =>
            {
                attempts.Add(locale);
                if (locale == "en")
                {
                    throw new CultureNotFoundException("name", locale, "unsupported");
                }

                return new MessageContext(locale, options);
            });

        Assert.Equal(["en", CultureInfo.InvariantCulture.Name], attempts);
        Assert.NotNull(context);
    }
}