// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Runtime;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class SecretRedactorTests
{
    [Theory]
    [InlineData(
        "connect \"AccountEndpoint=https://example.documents.azure.com/;AccountKey=example-key+/==;\"",
        "connect \"AccountEndpoint=https://example.documents.azure.com/;AccountKey=[REDACTED];\"")]
    [InlineData(
        "CONNECT 'AccountEndpoint=https://example.documents.azure.com/;accountkey = example-key+/==' --mode gateway",
        "CONNECT 'AccountEndpoint=https://example.documents.azure.com/;accountkey = [REDACTED]' --mode gateway")]
    [InlineData(
        "connect \"AccountEndpoint=https://example.documents.azure.com/;AccountKey=example \r\n key==;Database=items;\"",
        "connect \"AccountEndpoint=https://example.documents.azure.com/;AccountKey=[REDACTED];Database=items;\"")]
    [InlineData(
        """$connection = "AccountEndpoint=https://example.documents.azure.com/;AccountKey=example-key==;"; exec connect $connection""",
        """$connection = "AccountEndpoint=https://example.documents.azure.com/;AccountKey=[REDACTED];"; exec connect $connection""")]
    [InlineData(
        "connect 'AccountKey=first-key==;AccountEndpoint=https://example.documents.azure.com/;AccountKey=second-key==;'",
        "connect 'AccountKey=[REDACTED];AccountEndpoint=https://example.documents.azure.com/;AccountKey=[REDACTED];'")]
    [InlineData(
        """connect "AccountEndpoint=https://example.documents.azure.com/;AccountKey=example-key==""",
        """connect "AccountEndpoint=https://example.documents.azure.com/;AccountKey=[REDACTED]""")]
    [InlineData("""echo '{"AccountKey":"example-key=="}'""", """echo '{"AccountKey":"[REDACTED]"}'""")]
    [InlineData("connect 'ResourceToken=type=resource&sig=example-signature;'", "connect 'ResourceToken=[REDACTED];'")]
    [InlineData("connect 'SharedAccessKey=example-key==;'", "connect 'SharedAccessKey=[REDACTED];'")]
    [InlineData("echo 'Bearer example-token'", "echo 'Bearer [REDACTED]'")]
    [InlineData("echo 'https://example.test/?sig=example-signature&se=2030'", "echo 'https://example.test/?sig=[REDACTED]&se=2030'")]
    public void MaskSecrets_RemovesCredentialValuesAndIsIdempotent(string text, string expected)
    {
        var redacted = SecretRedactor.MaskHistory(text);

        Assert.Equal(expected, redacted);
        Assert.Equal(expected, SecretRedactor.MaskHistory(redacted));
    }

    [Theory]
    [InlineData("connect https://example.documents.azure.com/ --azure-cli")]
    [InlineData("connect")]
    [InlineData("query 'SELECT c.id, c.partitionKey FROM c'")]
    [InlineData("""mkitem '{"id":"item","partitionKey":"p1"}'""")]
    [InlineData("echo first\n echo second")]
    [InlineData("")]
    [InlineData("""query "SELECT * FROM c WHERE c.token = 123" """)]
    [InlineData("""query "SELECT * FROM c WHERE c.password = 'value' AND c.secret = 42" """)]
    [InlineData("echo token=123")]
    [InlineData("echo design=blue")]
    public void MaskSecrets_PreservesCommandsWithoutCredentials(string text)
    {
        Assert.Equal(text, SecretRedactor.MaskHistory(text));
    }

    [Theory]
    [InlineData("""connect "AccountEndpoint=https://example.documents.azure.com/;Account\u004Bey=example-key==;" """)]
    [InlineData("""connect "AccountEndpoint=https://example.documents.azure.com/;AccountKey=\"example-key==\";" """)]
    [InlineData("""connect "AccountEndpoint=https://example.documents.azure.com/;AccountKey=example\u002Dkey==;" """)]
    [InlineData("""$connection = "AccountEndpoint=https://example.documents.azure.com/;Account\u004Bey=example-key==;"; connect $connection""")]
    public void MaskHistory_RedactsDecodedStringCredentials(string command)
    {
        var masked = SecretRedactor.MaskHistory(command);

        Assert.DoesNotContain("example-key", masked);
        Assert.DoesNotContain("""example\u002Dkey""", masked);
        Assert.Contains("AccountKey=", masked);
        Assert.Contains("[REDACTED]", masked);
        Assert.Contains("AccountEndpoint=https://example.documents.azure.com/", masked);
        Assert.Equal(masked, SecretRedactor.MaskHistory(masked));
        var lexer = new Lexer(masked);
        _ = lexer.Tokenize().ToArray();
        Assert.False(lexer.Errors.HasErrors);
    }

    [Theory]
    [InlineData("""$credentials = {"Account\u004Bey": "example-key=="}""")]
    [InlineData("""$credentials = {"SharedAccess\u004Bey": "example-key=="}""")]
    [InlineData("""$credentials = {"Resource\u0054oken": "example-key=="}""")]
    [InlineData("""echo '{"Account\u004Bey": "example-key=="}'""")]
    [InlineData("""echo "{\"Account\\u004Bey\": \"example-key==\"}" """)]
    [InlineData("""connect 'AccountKey=''example-key=='';'""")]
    [InlineData("""$AccountKey='example-key==';""")]
    [InlineData("""$SharedAccessKey="example-key==";""")]
    [InlineData("""$ResourceToken='example-key==';""")]
    [InlineData("$credentials = {\"AccountKey\":\n\"example-key==\"}")]
    [InlineData("$credentials = {\"SharedAccessKey\"\n:\n\"example-key==\"}")]
    [InlineData("$credentials = {\"ResourceToken\": # credential\n\"example-key==\"}")]
    [InlineData("$AccountKey = # credential\n'example-key=='")]
    public void MaskHistory_RedactsEscapedPropertyNamesAndDoubledQuotes(string command)
    {
        var masked = SecretRedactor.MaskHistory(command);

        Assert.DoesNotContain("example-key", masked);
        Assert.Contains("[REDACTED]", masked);
        Assert.Equal(masked, SecretRedactor.MaskHistory(masked));
        var lexer = new Lexer(masked);
        _ = lexer.Tokenize().ToArray();
        Assert.False(lexer.Errors.HasErrors);
    }

    [Theory]
    [InlineData("""connect $"AccountEndpoint=$endpoint;Account\u004Bey=example-key==;" """)]
    [InlineData("""connect $"AccountEndpoint=$($endpoint);AccountKey=\"example-key==\";" """)]
    [InlineData("""connect $"AccountEndpoint=$endpoint;AccountKey=example-key==;Label=\$literal;" """)]
    public void MaskHistory_PreservesInterpolationAndNonSecretSource(string command)
    {
        var masked = SecretRedactor.MaskHistory(command);
        var expected = command.Replace("example-key==", "[REDACTED]", StringComparison.Ordinal);

        Assert.Equal(expected, masked);
        Assert.Equal(masked, SecretRedactor.MaskHistory(masked));
        var lexer = new Lexer(masked);
        var tokens = lexer.Tokenize().ToArray();
        Assert.False(lexer.Errors.HasErrors);
        Assert.Single(tokens, token => token.Type == TokenType.InterpolatedString);
    }

    [Theory]
    [InlineData("$('example-key==')")]
    [InlineData("$((('example-key==')))")]
    [InlineData("$('example-key==;with)delimiters')")]
    [InlineData("prefix$('example-key==')suffix")]
    [InlineData("$('example-key==')$('second-secret')")]
    public void MaskHistory_RedactsCompleteInterpolatedCredentialExpressions(string credential)
    {
        var command = "connect $\"AccountEndpoint=$endpoint;AccountKey=" + credential + ";Label=$label;\"";
        const string expected = "connect $\"AccountEndpoint=$endpoint;AccountKey=[REDACTED];Label=$label;\"";

        var masked = SecretRedactor.MaskHistory(command);

        Assert.Equal(expected, masked);
        Assert.Equal(masked, SecretRedactor.MaskHistory(masked));
        var lexer = new Lexer(masked);
        Assert.Single(lexer.Tokenize(), token => token.Type == TokenType.InterpolatedString);
        Assert.False(lexer.Errors.HasErrors);
    }

    [Theory]
    [InlineData("$('example-token')")]
    [InlineData("prefix$('example-token')suffix")]
    [InlineData("$((('example-token')))")]
    public void MaskHistory_RedactsCompleteInterpolatedBearerValues(string credential)
    {
        var command = "echo $\"Bearer " + credential + " Label=$label\"";
        const string expected = "echo $\"Bearer [REDACTED] Label=$label\"";

        var masked = SecretRedactor.MaskHistory(command);

        Assert.Equal(expected, masked);
        Assert.Equal(masked, SecretRedactor.MaskHistory(masked));
    }

    [Theory]
    [InlineData("""$AccountKey = ('example-key=='); echo 'unchanged'""", """$AccountKey = "[REDACTED]"; echo 'unchanged'""")]
    [InlineData("""$SharedAccessKey = (('example-key==')); echo 'unchanged'""", """$SharedAccessKey = "[REDACTED]"; echo 'unchanged'""")]
    [InlineData("""$ResourceToken = 'example-key==' + 'suffix'; echo 'unchanged'""", """$ResourceToken = "[REDACTED]"; echo 'unchanged'""")]
    [InlineData("""$data = {"AccountKey": ('example-key=='), "label": "unchanged"}""", """$data = {"AccountKey": "[REDACTED]", "label": "unchanged"}""")]
    [InlineData("""$AccountKey = ('example-key==') + ('suffix'); echo 'unchanged'""", """$AccountKey = "[REDACTED]"; echo 'unchanged'""")]
    public void MaskHistory_RedactsCompleteCredentialExpressionsAndPreservesSyntax(string command, string expected)
    {
        var masked = SecretRedactor.MaskHistory(command);

        Assert.Equal(expected, masked);
        Assert.Equal(masked, SecretRedactor.MaskHistory(masked));
        var parser = new StatementParser(masked);
        _ = parser.ParseStatements();
        Assert.False(parser.Errors.HasErrors);
    }
}
