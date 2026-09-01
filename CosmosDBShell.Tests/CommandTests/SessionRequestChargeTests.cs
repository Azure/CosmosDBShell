// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;
using Microsoft.Azure.Cosmos;
using Spectre.Console;

[Collection(ConsoleOutputTestCollection.Name)]
public class SessionRequestChargeTests
{
    [Fact]
    public void SessionVariables_ReflectCurrentUsage()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.RecordRequestCharge(new CommandState { RequestCharge = 2.5 });
        shell.RecordRequestCharge(new CommandState { RequestCharge = 1.25 });

        var charge = Assert.IsType<ShellDecimal>(shell.GetVariable("sessionRequestCharge"));
        var operationCount = Assert.IsType<ShellDecimal>(shell.GetVariable("sessionChargedOperationCount"));
        var maximum = Assert.IsType<ShellDecimal>(shell.GetVariable("sessionRequestChargeWarningThreshold"));

        Assert.Equal(3.75, charge.Value);
        Assert.Equal(2, operationCount.Value);
        Assert.Equal(0, maximum.Value);
    }

    [Fact]
    public void SessionUsageVariables_AreReadOnly()
    {
        using var shell = ShellInterpreter.CreateInstance();

        Assert.Throws<ShellException>(() => shell.SetVariable("sessionRequestCharge", new ShellNumber(1)));
        Assert.Throws<ShellException>(() => shell.SetVariable("sessionChargedOperationCount", new ShellNumber(1)));
    }

    [Fact]
    public void SessionRequestChargeWarningThreshold_WarnsOnlyOnceWhenReached()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.SetVariable("sessionRequestChargeWarningThreshold", new ShellDecimal(3));

        var output = CaptureConsole(() =>
        {
            shell.RecordRequestCharge(new CommandState { RequestCharge = 2 });
            shell.RecordRequestCharge(new CommandState { RequestCharge = 1 });
            shell.RecordRequestCharge(new CommandState { RequestCharge = 1 });
        });

        Assert.Equal(1, CountOccurrences(output, "has reached the configured warning threshold"));
    }

    [Fact]
    public void SessionRequestChargeWarningThreshold_ZeroDisablesWarning()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.SetVariable("sessionRequestChargeWarningThreshold", new ShellNumber(0));

        var output = CaptureConsole(() => shell.RecordRequestCharge(new CommandState { RequestCharge = 10 }));

        Assert.Empty(output);
    }

    [Fact]
    public void Connect_PreservesMaximumAndRearmsWarning()
    {
        using var shell = ShellInterpreter.CreateInstance();
        shell.SetVariable("sessionRequestChargeWarningThreshold", new ShellNumber(2));
        _ = CaptureConsole(() => shell.RecordRequestCharge(new CommandState { RequestCharge = 2 }));

        shell.Connect(CreateTestClient(), credentialTypeOverride: "AccountKey");
        var output = CaptureConsole(() => shell.RecordRequestCharge(new CommandState { RequestCharge = 2 }));

        Assert.Equal(2, Assert.IsType<ShellDecimal>(shell.GetVariable("sessionRequestChargeWarningThreshold")).Value);
        Assert.Equal(1, CountOccurrences(output, "has reached the configured warning threshold"));
    }

    [Fact]
    public void SessionRequestChargeWarningThreshold_RejectsInvalidValues()
    {
        using var shell = ShellInterpreter.CreateInstance();

        Assert.Throws<ShellException>(() => shell.SetVariable("sessionRequestChargeWarningThreshold", new ShellDecimal(-1)));
        Assert.Throws<ShellException>(() => shell.SetVariable("sessionRequestChargeWarningThreshold", new ShellText("ten")));
    }

    private static CosmosClient CreateTestClient() => new(
        "https://localhost:8081",
        Convert.ToBase64String(new byte[64]),
        new CosmosClientOptions());

    private static int CountOccurrences(string value, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }

    private static string CaptureConsole(Action action)
    {
        var saved = AnsiConsole.Console;
        using var writer = new StringWriter();
        try
        {
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(writer),
            });

            action();
        }
        finally
        {
            AnsiConsole.Console = saved;
        }

        return writer.ToString();
    }
}
