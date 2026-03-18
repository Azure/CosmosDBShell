// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Parser;
using Azure.Data.Cosmos.Shell.Core;

namespace CosmosShell.Tests.Parser;

public class StatementTests
{
    private Statement ParseStatement(string input)
    {
        var lexer = new Lexer(input);
        var parser = new StatementParser(lexer);
        var statements = parser.ParseStatements();
        Assert.Single(statements);
        return statements[0];
    }

    private async Task<CommandState> RunStatementAsync(string input, ShellInterpreter? shell = null, CommandState? state = null)
    {
        var statement = ParseStatement(input);
        shell ??= ShellInterpreter.Instance;
        state ??= new CommandState();
        return await statement.RunAsync(shell, state, CancellationToken.None);
    }

    [Fact]
    public void ParseStatement_Assignment_ReturnsAssignmentStatement()
    {
        var stmt = ParseStatement("$x = 42");
        Assert.NotNull(stmt);
        Assert.IsType<AssignmentStatement>(stmt);
    }


    [Fact]
    public void ParseStatement_If_ReturnsIfStatement()
    {
        var stmt = ParseStatement("if true $x = 1 else $x = 2");
        Assert.NotNull(stmt);
        Assert.IsType<IfStatement>(stmt);
    }

    [Fact]
    public void ParseStatement_While_ReturnsWhileStatement()
    {
        var stmt = ParseStatement("while false $x = 1");
        Assert.NotNull(stmt);
        Assert.IsType<WhileStatement>(stmt);
    }

    [Fact]
    public void ParseStatement_Block_ReturnsBlockStatement()
    {
        var stmt = ParseStatement("{ $x = 1; $y = 2 }");
        Assert.NotNull(stmt);
        Assert.IsType<BlockStatement>(stmt);
    }

    [Fact]
    public void ParseStatement_Command_ReturnsCommandStatement()
    {
        var stmt = ParseStatement("echo \"hello\"");
        Assert.NotNull(stmt);
        Assert.IsType<CommandStatement>(stmt);
    }

    [Fact]
    public void ParseStatement_Def_ReturnsDefStatement()
    {
        var stmt = ParseStatement("def myfunc [a] $x = a");
        Assert.NotNull(stmt);
        Assert.IsType<DefStatement>(stmt);
    }

    [Fact]
    public void ParseStatement_For_ReturnsForStatement()
    {
        var stmt = ParseStatement("for $i in $foo $x = $i");
        Assert.NotNull(stmt);
        Assert.IsType<ForStatement>(stmt);
    }

    [Fact]
    public void ParseStatement_DoWhile_ReturnsDoWhileStatement()
    {
        var stmt = ParseStatement("do $x = 1 while false");
        Assert.NotNull(stmt);
        Assert.IsType<DoWhileStatement>(stmt);
    }

    [Fact]
    public void ParseStatement_Loop_ReturnsLoopStatement()
    {
        var stmt = ParseStatement("loop $x = 1");
        Assert.NotNull(stmt);
        Assert.IsType<LoopStatement>(stmt);
    }

    [Fact]
    public void ParseStatement_Return_ReturnsReturnStatement()
    {
        var stmt = ParseStatement("return 42");
        Assert.NotNull(stmt);
        Assert.IsType<ReturnStatement>(stmt);
    }

    [Fact]
    public void ParseStatement_Break_ReturnsBreakStatement()
    {
        var stmt = ParseStatement("break");
        Assert.NotNull(stmt);
        Assert.IsType<BreakStatement>(stmt);
    }

    [Fact]
    public void ParseStatement_Continue_ReturnsContinueStatement()
    {
        var stmt = ParseStatement("continue");
        Assert.NotNull(stmt);
        Assert.IsType<ContinueStatement>(stmt);
    }

    [Fact]
    public void ParseStatement_Block()
    {
        var stmt = ParseStatement("{ echo \"foo\"\necho \"bar\"\n }");
        Assert.NotNull(stmt);
        Assert.IsType<BlockStatement>(stmt);

        // Verify the block contains the expected statement
        var blockStmt = (BlockStatement)stmt;

        // Verify the echo command was parsed correctly
        var echoStmt = blockStmt.Statements[0];
        Assert.IsType<CommandStatement>(echoStmt);

        var cmdStmt = (CommandStatement)echoStmt;
        Assert.Equal("echo", cmdStmt.Name);
        Assert.Single(cmdStmt.Arguments);

        // Verify the argument is a string constant "foo"
        var arg = cmdStmt.Arguments[0];
        Assert.IsType<ConstantExpression>(arg);

        var constExpr = (ConstantExpression)arg;
        Assert.IsType<ShellText>(constExpr.Value);

        echoStmt = blockStmt.Statements[1];
        Assert.IsType<CommandStatement>(echoStmt);

        cmdStmt = (CommandStatement)echoStmt;
        Assert.Equal("echo", cmdStmt.Name);
        Assert.Single(cmdStmt.Arguments);

        // Verify the argument is a string constant "foo"
        arg = cmdStmt.Arguments[0];
        Assert.IsType<ConstantExpression>(arg);

        constExpr = (ConstantExpression)arg;
        Assert.IsType<ShellText>(constExpr.Value);

    }

    [Fact]
    public void ParseStatement_Pipe_ReturnsPipeStatement()
    {
        var stmt = ParseStatement("echo \"hello\" | grep \"ll\"");
        Assert.NotNull(stmt);
        Assert.IsType<PipeStatement>(stmt);

        var pipeStmt = (PipeStatement)stmt;
        Assert.Equal(2, pipeStmt.Statements.Count);

        // First statement should be echo
        var firstStmt = pipeStmt.Statements[0];
        Assert.IsType<CommandStatement>(firstStmt);
        var echoCmd = (CommandStatement)firstStmt;
        Assert.Equal("echo", echoCmd.Name);
        Assert.Single(echoCmd.Arguments);

        // Second statement should be grep
        var secondStmt = pipeStmt.Statements[1];
        Assert.IsType<CommandStatement>(secondStmt);
        var grepCmd = (CommandStatement)secondStmt;
        Assert.Equal("grep", grepCmd.Name);
        Assert.Single(grepCmd.Arguments);
    }

    [Fact]
    public void ParseStatement_MultiplePipes_ReturnsPipeStatement()
    {
        var stmt = ParseStatement("cat file.txt | grep \"error\" | sort | head -n 10");
        Assert.NotNull(stmt);
        Assert.IsType<PipeStatement>(stmt);

        var pipeStmt = (PipeStatement)stmt;
        Assert.Equal(4, pipeStmt.Statements.Count);

        // Verify each command in the pipeline
        var commands = pipeStmt.Statements.Cast<CommandStatement>().ToArray();
        Assert.Equal("cat", commands[0].Name);
        Assert.Equal("grep", commands[1].Name);
        Assert.Equal("sort", commands[2].Name);
        Assert.Equal("head", commands[3].Name);
    }

    [Fact]
    public void ParseStatement_PipeWithNewlines_ReturnsPipeStatement()
    {
        var stmt = ParseStatement("echo \"hello\" |\n  grep \"ll\"");
        Assert.NotNull(stmt);
        Assert.IsType<PipeStatement>(stmt);

        var pipeStmt = (PipeStatement)stmt;
        Assert.Equal(2, pipeStmt.Statements.Count);
    }

    [Fact]
    public void ParseStatement_PipeInBlock_ReturnsBlockWithPipe()
    {
        var stmt = ParseStatement("{ echo \"foo\" | grep \"o\" }");
        Assert.NotNull(stmt);
        Assert.IsType<BlockStatement>(stmt);

        var blockStmt = (BlockStatement)stmt;
        Assert.Single(blockStmt.Statements);

        var pipeStmt = blockStmt.Statements[0];
        Assert.IsType<PipeStatement>(pipeStmt);

        var pipe = (PipeStatement)pipeStmt;
        Assert.Equal(2, pipe.Statements.Count);
    }

    [Fact]
    public void ParseStatement_PipeWithAssignment_ReturnsPipeStatement()
    {
        var stmt = ParseStatement("$x = 42 | echo $x");
        Assert.NotNull(stmt);
        Assert.IsType<PipeStatement>(stmt);

        var pipeStmt = (PipeStatement)stmt;
        Assert.Equal(2, pipeStmt.Statements.Count);

        // First should be assignment
        Assert.IsType<AssignmentStatement>(pipeStmt.Statements[0]);

        // Second should be echo command
        Assert.IsType<CommandStatement>(pipeStmt.Statements[1]);
    }
}