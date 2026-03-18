// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// ------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Commands;
using Azure.Data.Cosmos.Shell.Core;
using Azure.Data.Cosmos.Shell.Parser;

using Xunit;

namespace CosmosShell.Tests.Parser
{
    public enum Mode
    {
        Alpha,
        Beta,
        Gamma
    }

    [CosmosCommand("optcmd")]
    internal class OptionBindingCommand : CosmosCommand
    {
        [CosmosOption("foo")]
        public string? Foo { get; init; }

        [CosmosOption("flag")]
        public bool Flag { get; init; }

        [CosmosOption("mode")]
        public Mode? Mode { get; init; }

        [CosmosParameter("arg1", IsRequired = false)]
        public string? Arg1 { get; init; }

        [CosmosParameter("rest", IsRequired = false)]
        public string[]? Rest { get; init; }

        public override Task<CommandState> ExecuteAsync(ShellInterpreter shell, CommandState commandState, string commandText, CancellationToken token)
            => Task.FromResult(commandState); // Not executed in these tests
    }

    public class CommandOptionBindingTests
    {
        private readonly ShellInterpreter shell;

        public CommandOptionBindingTests()
        {
            this.shell = ShellInterpreter.CreateInstance();

            if (CommandFactory.TryCreateFactory(typeof(OptionBindingCommand), out var factory))
            {
                shell.App.Commands["optcmd"] = factory;
            }
        }

        private static CommandStatement Parse(string text)
        {
            var parser = new StatementParser(text);
            var stmts = parser.ParseStatements();
            var stmt = Assert.Single(stmts);
            return Assert.IsType<CommandStatement>(stmt);
        }

        private async Task<OptionBindingCommand> BindAsync(string text)
        {
            var stmt = Parse(text);
            Assert.Equal("optcmd", stmt.Name);
            Assert.True(shell.App.Commands.TryGetValue("optcmd", out var factory));
            var cmd = await stmt.CreateCommandAsync(factory, shell, new CommandState(), CancellationToken.None);
            return Assert.IsType<OptionBindingCommand>(cmd);
        }

        [Fact]
        public async Task SpaceSeparatedValue_ForStringOption_IsConsumed()
        {
            var cmd = await BindAsync("optcmd -foo bar arg1 extra1 extra2");
            Assert.Equal("bar", cmd.Foo);
            Assert.False(cmd.Flag);
            Assert.Null(cmd.Mode);
            Assert.Equal("arg1", cmd.Arg1);
            Assert.Equal(new[] { "extra1", "extra2" }, cmd.Rest);
        }

        [Fact]
        public async Task ColonValue_ForStringOption_IsBound()
        {
            var cmd = await BindAsync("optcmd -foo:bar arg1");
            Assert.Equal("bar", cmd.Foo);
            Assert.Equal("arg1", cmd.Arg1);
            Assert.Null(cmd.Rest);
        }

        [Fact]
        public async Task DoubleDashColonValue_ForStringOption_IsBound()
        {
            var cmd = await BindAsync("optcmd --foo:bar arg1");
            Assert.Equal("bar", cmd.Foo);
            Assert.Equal("arg1", cmd.Arg1);
        }

        [Fact]
        public async Task EqualsValue_WithVariable_IsEvaluated()
        {
            var assignResult = await this.shell.ExecuteCommandAsync("$foo = \"bar\"", CancellationToken.None);
            Assert.False(assignResult.IsError);

            var cmd = await BindAsync("optcmd --foo=$foo arg1");
            Assert.Equal("bar", cmd.Foo);
            Assert.Equal("arg1", cmd.Arg1);
        }

        [Fact]
        public async Task QuotedSpaceSeparatedValue_IsConsumed()
        {
            var cmd = await BindAsync("optcmd -foo \"complex value\" arg1");
            Assert.Equal("complex value", cmd.Foo);
            Assert.Equal("arg1", cmd.Arg1);
        }

        [Fact]
        public async Task BooleanOption_PresenceSetsTrue_NextTokenBecomesArgument()
        {
            var cmd = await BindAsync("optcmd -flag true argX");
            Assert.True(cmd.Flag);
            Assert.Equal("true", cmd.Arg1);
            Assert.Equal(new[] { "argX" }, cmd.Rest);
        }

        [Fact]
        public async Task BooleanOption_Alone_SetsTrue()
        {
            var cmd = await BindAsync("optcmd -flag arg1 extra");
            Assert.True(cmd.Flag);
            Assert.Equal("arg1", cmd.Arg1);
            Assert.Equal(new[] { "extra" }, cmd.Rest);
        }

        [Fact]
        public async Task EnumOption_SpaceSeparatedValue_IsConsumed()
        {
            var cmd = await BindAsync("optcmd -mode Beta arg1 tail");
            Assert.Equal(Mode.Beta, cmd.Mode);
            Assert.Equal("arg1", cmd.Arg1);
            Assert.Equal(new[] { "tail" }, cmd.Rest);
        }

        [Fact]
        public async Task EnumOption_ColonValue_IsBound()
        {
            var cmd = await BindAsync("optcmd -mode:Gamma arg1");
            Assert.Equal(Mode.Gamma, cmd.Mode);
            Assert.Equal("arg1", cmd.Arg1);
        }

        [Fact]
        public async Task ParseStructure_MultipleOptions_MixedForms()
        {
            var stmt = Parse("optcmd -flag -mode Alpha -foo value arg1 r1 r2");
            var cmd = Assert.IsType<CommandStatement>(stmt);

            // Raw parse (before binding) – verify argument sequence and option tokens.
            // Space–separated option values (Alpha, value) are NOT attached yet; they appear as separate expressions.
            var args = cmd.Arguments;

            // 1. Option tokens in order
            Assert.IsType<CommandOption>(args[0]); // -flag
            Assert.IsType<CommandOption>(args[1]); // -mode
            Assert.IsType<CommandOption>(args[3]); // -foo

            var optFlag = (CommandOption)args[0];
            var optMode = (CommandOption)args[1];
            var optFoo = (CommandOption)args[3];

            Assert.Equal("flag", optFlag.Name);
            Assert.Null(optFlag.Value); // boolean option has no inline value

            Assert.Equal("mode", optMode.Name);
            Assert.Null(optMode.Value); // space-separated value "Alpha" not yet bound

            Assert.Equal("foo", optFoo.Name);
            Assert.Null(optFoo.Value); // space-separated value "value" not yet bound

            // 2. Space-separated literals immediately following non-boolean options
            Assert.Equal("Alpha", args[2].ToString());
            Assert.Equal("value", args[4].ToString());

            // 3. Remaining positional arguments
            Assert.Equal("arg1", args[5].ToString());
            Assert.Equal("r1", args[6].ToString());
            Assert.Equal("r2", args[7].ToString());

            // 4. After binding via CreateCommandAsync the space-separated values are consumed
            Assert.True(shell.App.Commands.TryGetValue("optcmd", out var factory));
            var bound = await cmd.CreateCommandAsync(factory, shell, new CommandState(), CancellationToken.None);
            var boundCmd = Assert.IsType<OptionBindingCommand>(bound);

            Assert.True(boundCmd.Flag);
            Assert.Equal(Mode.Alpha, boundCmd.Mode);
            Assert.Equal("value", boundCmd.Foo);
            Assert.Equal("arg1", boundCmd.Arg1);
            Assert.Equal(new[] { "r1", "r2" }, boundCmd.Rest);
        }

        [Fact]
        public async Task MissingValue_ForNonBooleanOption_Throws()
        {
            var stmt = Parse("optcmd -foo -flag arg1");
            Assert.True(shell.App.Commands.TryGetValue("optcmd", out var factory));
            var ex = await Assert.ThrowsAsync<CommandException>(async () => await stmt.CreateCommandAsync(factory, ShellInterpreter.Instance, new CommandState(), CancellationToken.None));
            Assert.Contains("requires a value", ex.Message);
        }

        [Fact]
        public async Task ArrayRestParameters_CollectRemainingArguments()
        {
            var cmd = await BindAsync("optcmd -foo first main rest1 rest2 rest3");
            Assert.Equal("first", cmd.Foo);
            Assert.Equal("main", cmd.Arg1);
            Assert.Equal(new[] { "rest1", "rest2", "rest3" }, cmd.Rest);
        }
    }
}