// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

namespace CosmosShell.Tests.Runtime;

public class LoopTests
{
    private ShellInterpreter CreateInterpreter()
    {
        return new ShellInterpreter();
    }

    [Fact]
    public async Task ForLoop_IteratesOverJsonArray_ProcessesEachElement()
    {
        // Arrange
        var interpreter = CreateInterpreter();
        var script = @"
            $sum = 0
            $output = """"
            $numbers = [1, 2, 3]
            for $num in $numbers {
                $sum = $sum + $num
                $output = $output + $num + "" ""
            }
        ";

        // Act
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();

        foreach (var statement in statements)
        {
            await statement.RunAsync(interpreter, new CommandState(), CancellationToken.None);
        }

        // Assert
        var sum = interpreter.GetVariable("sum");
        var output = interpreter.GetVariable("output");

        Assert.NotNull(sum);
        Assert.IsType<ShellNumber>(sum);
        Assert.Equal(6, ((ShellNumber)sum).Value); // 1 + 2 + 3 = 6

        Assert.NotNull(output);
        Assert.IsType<ShellText>(output);
        Assert.Equal("1 2 3 ", ((ShellText)output).Text);
    }

}
