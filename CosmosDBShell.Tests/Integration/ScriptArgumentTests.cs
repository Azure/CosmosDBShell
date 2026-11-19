// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Integration;

using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

public class ScriptArgumentTests : IntegrationTestBase
{
    [Fact]
    public async Task Script_MultiLineWithControlFlow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CosmosShellIntTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "control_flow.csh");
        await File.WriteAllTextAsync(scriptPath, "$sum = 0\nfor $i in [1, 2, 3] {\n  $sum = ($sum + $i)\n}\nreturn $sum\ndef unreachable { return 0 }\n", TestContext.Current.CancellationToken);

        try
        {
            var cmd = new CommandStatement(new Token(TokenType.Identifier, scriptPath, 0, scriptPath.Length));
            var state = await cmd.RunAsync(Shell, new CommandState(), CancellationToken.None);

            Assert.False(state.IsError);
            Assert.Equal(6, Assert.IsType<ShellNumber>(state.Result).Value);
            Assert.False(Shell.Functions.ContainsKey("unreachable"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
