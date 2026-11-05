// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using System.Net;
using System.Reflection;
using System.Text.Json;
using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.States;
using Microsoft.Azure.Cosmos;
using NSubstitute;

public class CommandUsageErrorTests
{
    [Theory]
    [InlineData(nameof(ParameterValues.Number), "invalid")]
    [InlineData(nameof(ParameterValues.Number), "999999999999999999999")]
    [InlineData(nameof(ParameterValues.OptionalNumber), "invalid")]
    [InlineData(nameof(ParameterValues.OptionalNumber), "999999999999999999999")]
    [InlineData(nameof(ParameterValues.Fraction), "invalid")]
    [InlineData(nameof(ParameterValues.Format), "invalid")]
    public void PositionalConversion_IsUsageError(string property, string input)
    {
        var target = new ParameterValues();
        var error = Assert.Throws<CommandException>(
            () => Parameter.SetValue(target, typeof(ParameterValues).GetProperty(property)!, input));

        Assert.Equal(ShellExitCode.UsageError, ShellExitCode.FromException(error));
    }

    [Fact]
    public void PositionalConversion_PreservesValidValues()
    {
        var target = new ParameterValues();
        Parameter.SetValue(target, typeof(ParameterValues).GetProperty(nameof(ParameterValues.Number))!, "42");
        Parameter.SetValue(target, typeof(ParameterValues).GetProperty(nameof(ParameterValues.OptionalNumber))!, "42");
        Parameter.SetValue(target, typeof(ParameterValues).GetProperty(nameof(ParameterValues.Format))!, "json");

        Assert.Equal(42, target.Number);
        Assert.Equal(42, target.OptionalNumber);
        Assert.Equal(OutputFormat.JSon, target.Format);
    }

    public static IEnumerable<object[]> BindingCases()
    {
        foreach (var type in typeof(CosmosCommand).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<CosmosCommandAttribute>() != null))
        {
            if (!CommandFactory.TryCreateFactory(type, out var factory) || factory.IsExternal)
            {
                continue;
            }

            var name = factory.CommandName;
            yield return [type, $"{name} --unknown-usage-test-option"];
            if (factory.Parameters.Any(parameter => parameter.IsRequired))
            {
                yield return [type, name];
            }

            if (factory.Parameters.All(parameter => !parameter.PropertyInfo.PropertyType.IsArray))
            {
                yield return [type, name + string.Concat(Enumerable.Repeat(" value", factory.Parameters.Count + 1))];
            }

            foreach (var option in factory.AllOptions)
            {
                var targetType = Nullable.GetUnderlyingType(option.PropertyInfo.PropertyType) ?? option.PropertyInfo.PropertyType;
                if (targetType != typeof(string))
                {
                    yield return [type, $"{name} --{option.Name[0]}=invalid-usage-test-value"];
                }

                if (!option.IsBool && option.DefaultValue == null)
                {
                    yield return [type, $"{name} --{option.Name[0]}"];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(BindingCases))]
    public async Task EveryBuiltInCommand_InvalidBindingIsUsageError(Type type, string text)
    {
        using var shell = ShellInterpreter.CreateInstance();
        Assert.True(CommandFactory.TryCreateFactory(type, out var factory));
        var statement = Assert.IsType<CommandStatement>(Assert.Single(new StatementParser(text).ParseStatements()));

        var error = await Assert.ThrowsAnyAsync<CommandException>(
            () => statement.CreateCommandAsync(factory, shell, new CommandState(), TestContext.Current.CancellationToken));

        Assert.Equal(ShellExitCode.UsageError, ShellExitCode.FromException(error));
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("filter '.id'")]
    [InlineData("echo [1] | filter 'unknown()'")]
    [InlineData("echo [1] | ftab --format invalid")]
    [InlineData("echo [1] | ftab --take=-1")]
    [InlineData("echo [1] | ftab --sort 'value:invalid'")]
    [InlineData("echo [1] | ftab --colorize invalid")]
    [InlineData("echo [1] | ftab --format html --colorize 'value:1:red'")]
    [InlineData("import missing.json --dry-run --concurrency 0")]
    [InlineData("import missing.json --dry-run --concurrency=-1")]
    [InlineData("import missing.json --dry-run --mode invalid")]
    [InlineData("connect https://example.documents.azure.com --mode invalid")]
    [InlineData("doctor invalid")]
    [InlineData("doctor --timeout-ms=-1")]
    [InlineData("info --format invalid")]
    [InlineData("create invalid")]
    [InlineData("delete invalid")]
    [InlineData("batch invalid")]
    [InlineData("bulk invalid")]
    [InlineData("bulk run --concurrency=0")]
    [InlineData("bulk patch --where ''")]
    [InlineData("bulk patch --where 'true' --operations '[]'")]
    [InlineData("bulk run --dry-run --journal file.json")]
    [InlineData("sproc invalid")]
    [InlineData("trigger invalid")]
    [InlineData("udf invalid")]
    [InlineData("patch item p1 invalid /value 1")]
    [InlineData("patch item p1 set invalid-path 1")]
    [InlineData("patch item p1 increment /value not-a-number")]
    [InlineData("rm")]
    [InlineData("rm '*' --etag value")]
    [InlineData("watch --interval=NaN")]
    public async Task CommandValidation_ReturnsUsageError(string text)
    {
        using var shell = ShellInterpreter.CreateInstance();
        var client = Substitute.For<CosmosClient>();
        client.Endpoint.Returns(new Uri("https://example.documents.azure.com"));
        var database = Substitute.For<Database>();
        var container = Substitute.For<Container>();
        client.GetDatabase("db").Returns(database);
        database.GetContainer("items").Returns(container);
        var databaseResponse = Substitute.For<DatabaseResponse>();
        databaseResponse.StatusCode.Returns(HttpStatusCode.OK);
        database.ReadAsync(cancellationToken: Arg.Any<CancellationToken>()).Returns(databaseResponse);
        var containerResponse = Substitute.For<ContainerResponse>();
        containerResponse.StatusCode.Returns(HttpStatusCode.OK);
        containerResponse.Resource.Returns(new ContainerProperties("items", "/pk"));
        container.ReadContainerAsync(cancellationToken: Arg.Any<CancellationToken>()).Returns(containerResponse);
        shell.State = new ContainerState("items", "db", client);

        var error = await Assert.ThrowsAnyAsync<CommandException>(
            () => shell.RunCommandAsync(new CommandState(), text, TestContext.Current.CancellationToken));

        Assert.Equal(ShellExitCode.UsageError, ShellExitCode.FromException(error));
    }

    [Theory]
    [InlineData("info --db")]
    [InlineData("info --unknown-usage-test-option")]
    [InlineData("ftab --take=invalid")]
    [InlineData("print a b extra")]
    [InlineData("filter")]
    public async Task ExpressionsAndExec_PreserveUsageClassification(string command)
    {
        using var shell = ShellInterpreter.CreateInstance();
        foreach (var text in new[] { $"$value = ({command})", $"exec {command}" })
        {
            var error = await Record.ExceptionAsync(
                () => shell.RunCommandAsync(new CommandState(), text, TestContext.Current.CancellationToken));

            Assert.NotNull(error);
            Assert.Equal(ShellExitCode.UsageError, ShellExitCode.FromException(error));
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"op":"invalid"}""")]
    [InlineData("""{"op":"delete"}""")]
    [InlineData("""{"op":"create","item":1}""")]
    [InlineData("""{"op":"patch","id":"a","operations":[]}""")]
    public void BatchInputValidation_IsUsageError(string input)
    {
        var error = Assert.Throws<CommandException>(() => BatchOperationParser.Parse("batch", input));

        Assert.Equal(ShellExitCode.UsageError, ShellExitCode.FromException(error));
    }

    [Theory]
    [InlineData("""{"op":"create","item":{"id":"a","pk":"p"},"ifMatch":1}""")]
    [InlineData("""{"op":"delete","id":"a","partitionKey":[]}""")]
    public void BulkInputValidation_IsUsageError(string input)
    {
        using var document = JsonDocument.Parse(input);
        var error = Assert.Throws<CommandException>(
            () => BulkOperationNormalizer.Normalize(document.RootElement, ["/pk"], null, 0));

        Assert.Equal(ShellExitCode.UsageError, ShellExitCode.FromException(error));
    }

    private sealed class ParameterValues
    {
        public int Number { get; set; }

        public int? OptionalNumber { get; set; }

        public double Fraction { get; set; }

        public OutputFormat? Format { get; set; }
    }
}
