// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

using Azure.Data.Cosmos.Shell.Parser;

namespace CosmosShell.Tests.Parser;

/// <summary>
/// Tests for ToString() methods in the parser infrastructure.
/// These tests verify that AST nodes produce human-readable debug output.
/// </summary>
public class ToStringTests : TestBase
{
    [Fact]
    public void IfStatement_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "if $x > 5 { echo \"big\" } else { echo \"small\" }";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var ifStmt = Assert.IsType<IfStatement>(statements[0]);
        var result = ifStmt.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("if", result);
        Assert.Contains("else", result);
    }

    [Fact]
    public void WhileStatement_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "while $count < 10 { $count = $count + 1 }";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var whileStmt = Assert.IsType<WhileStatement>(statements[0]);
        var result = whileStmt.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("while", result);
    }

    [Fact]
    public void DoWhileStatement_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "do { echo $count; $count = $count + 1 } while ($count < 10)";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var doWhileStmt = Assert.IsType<DoWhileStatement>(statements[0]);
        var result = doWhileStmt.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("do", result);
        Assert.Contains("while", result);
    }

    [Fact]
    public void ForStatement_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "for $item in [1, 2, 3] { echo $item }";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var forStmt = Assert.IsType<ForStatement>(statements[0]);
        var result = forStmt.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("for", result);
        Assert.Contains("$item", result);
        Assert.Contains("in", result);
    }

    [Fact]
    public void LoopStatement_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "loop { break }";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var loopStmt = Assert.IsType<LoopStatement>(statements[0]);
        var result = loopStmt.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("loop", result);
    }

    [Fact]
    public void BreakStatement_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "break";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var breakStmt = Assert.IsType<BreakStatement>(statements[0]);
        var result = breakStmt.ToString();

        // Assert
        Assert.Equal("break", result);
    }

    [Fact]
    public void ContinueStatement_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "continue";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var continueStmt = Assert.IsType<ContinueStatement>(statements[0]);
        var result = continueStmt.ToString();

        // Assert
        Assert.Equal("continue", result);
    }

    [Fact]
    public void ReturnStatement_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "return 42";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var returnStmt = Assert.IsType<ReturnStatement>(statements[0]);
        var result = returnStmt.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("return", result);
    }

    [Fact]
    public void AssignmentStatement_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "$x = 42";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var assignStmt = Assert.IsType<AssignmentStatement>(statements[0]);
        var result = assignStmt.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("$x", result);
        Assert.Contains("=", result);
    }

    [Fact]
    public void BinaryOperatorExpression_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "$x = 5 + 3";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var assignStmt = Assert.IsType<AssignmentStatement>(statements[0]);
        var result = assignStmt.Value.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("+", result);
    }

    [Fact]
    public void VariableExpression_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "$myVariable = 10";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var assignStmt = Assert.IsType<AssignmentStatement>(statements[0]);
        var result = assignStmt.Variable.ToString();

        // Assert
        Assert.Equal("$myVariable", result);
    }

    [Fact]
    public void BlockStatement_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "{ echo \"hello\"; echo \"world\" }";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var blockStmt = Assert.IsType<BlockStatement>(statements[0]);
        var result = blockStmt.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("{", result);
        Assert.Contains("}", result);
    }

    [Fact]
    public void PipeStatement_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "echo \"test\" | cat";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var pipeStmt = Assert.IsType<PipeStatement>(statements[0]);
        var result = pipeStmt.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("|", result);
    }

    [Fact]
    public void JsonArrayExpression_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "$arr = [1, 2, 3]";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var assignStmt = Assert.IsType<AssignmentStatement>(statements[0]);
        var result = assignStmt.Value.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("[", result);
        Assert.Contains("]", result);
    }

    [Fact]
    public void ParensExpression_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = "$x = (5 + 3)";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var assignStmt = Assert.IsType<AssignmentStatement>(statements[0]);
        var result = assignStmt.Value.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("(", result);
        Assert.Contains(")", result);
    }

    [Fact]
    public void ComplexStatement_ToString_ReturnsReadableOutput()
    {
        // Arrange
        var script = @"
            if $x > 10 {
                for $i in [1, 2, 3] {
                    if $i == 2 {
                        continue
                    }
                    echo $i
                }
            }
        ";
        var parser = new StatementParser(script);

        // Act
        var statements = parser.ParseStatements();
        var ifStmt = Assert.IsType<IfStatement>(statements[0]);
        var result = ifStmt.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("if", result);
        Assert.Contains("for", result);
        // The result should be a valid representation of the complex nested structure
    }
}
