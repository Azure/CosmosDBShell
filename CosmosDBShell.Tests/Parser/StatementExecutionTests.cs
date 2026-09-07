// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.Parser;

using System;
using System.IO;
using System.Threading.Tasks;

using Azure.Data.Cosmos.Shell.Parser;

/// <summary>
/// Executes whole scripts to drive the <c>RunAsync</c> branches of control-flow
/// statements (if/else, while, do-while, for, loop, break, continue, return, blocks)
/// together with the condition-evaluation paths in the expression parser.
/// </summary>
public class StatementExecutionTests : TestBase
{
    public static System.Collections.Generic.IEnumerable<object[]> ValueOriginCases()
    {
        var operations = new (string Left, string Operator, string Right, string Type, string Expected)[]
        {
            ("\"2\"", "+", "\"2\"", "Text", "22"),
            ("\"hello\"", "+", "\"world\"", "Text", "helloworld"),
            ("\"\"", "+", "\"text\"", "Text", "text"),
            ("\"value=\"", "+", "2", "Text", "value=2"),
            ("2", "+", "\"px\"", "Text", "2px"),
            ("3", "+", "2", "Number", "5"),
            ("3.5", "+", "2", "Decimal", "5.5"),
            ("3", "/", "2", "Number", "1"),
            ("3.0", "/", "2", "Decimal", "1.5"),
            ("[1]", "+", "[2]", "Json", "[1,2]"),
            ("\"a\"", "==", "\"a\"", "Boolean", "true"),
            ("true", "&&", "false", "Boolean", "false"),
        };

        foreach (var operation in operations)
        {
            var leftOrigins = new[] { operation.Left, "$source.left", "$leftItem", "(identity $source.left)" };
            var rightOrigins = new[] { operation.Right, "$source.right", "$rightItem", "(identity $source.right)" };
            foreach (var left in leftOrigins)
            {
                foreach (var right in rightOrigins)
                {
                    var script = $"def identity [value] {{ return $value }}; " +
                        $"for $leftItem in $source.leftItems {{ for $rightItem in $source.rightItems {{ " +
                        $"$actual = {left} {operation.Operator} {right} }} }}";
                    var source = $"{{\"left\":{operation.Left},\"right\":{operation.Right}," +
                        $"\"leftItems\":[{operation.Left}],\"rightItems\":[{operation.Right}]}}";
                    yield return [source, script, operation.Type, operation.Expected];
                    yield return [source, $"$source = {source}; " + script, operation.Type, operation.Expected];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ValueOriginCases))]
    public async Task Operators_PreserveResultsAcrossValueOrigins(string source, string script, string expectedType, string expected)
    {
        using var document = System.Text.Json.JsonDocument.Parse(source);
        SetVariable("source", new ShellJson(document.RootElement.Clone()));
        var state = await Shell.RunCommandAsync(new(), script, TestContext.Current.CancellationToken);
        Assert.False(state.IsError);
        var actual = GetVariable("actual")!;
        Assert.Equal(expectedType, actual.DataType.ToString());
        Assert.Equal(expected, actual.ConvertShellObject(DataType.Text));
    }

    [Theory]
    [InlineData("3.0", 1.5)]
    [InlineData("-3.0", -1.5)]
    [InlineData("0.0", 0.0)]
    [InlineData("1.5 * 2", 1.5)]
    [InlineData("3.5", 1.75)]
    public async Task DecimalValues_SurviveRepeatedJsonConstruction(string expression, double expected)
    {
        var script = $"$initial = {expression}; $object = {{\"value\":$initial}}; " +
            "$array = [$object.value]; for $item in $array { " +
            "$rebuilt = {\"value\":$item}; $result = $rebuilt.value / 2 }";
        var state = await Shell.RunCommandAsync(new(), script, TestContext.Current.CancellationToken);
        Assert.False(state.IsError);
        Assert.Equal(expected, Assert.IsType<ShellDecimal>(GetVariable("result")).Value);
        var rebuilt = Assert.IsType<ShellJson>(GetVariable("rebuilt"));
        Assert.IsType<ShellDecimal>(ShellNumber.FromJson(rebuilt.Value.GetProperty("value")));
    }

    [Fact]
    public async Task SyntaxError_PreventsEarlierAssignment()
    {
        SetVariable("value", new ShellNumber(1));
        var state = await Shell.RunCommandAsync(new(), "$value = 2; if true {", System.Threading.CancellationToken.None);

        Assert.True(state.IsError);
        Assert.Equal(1, GetInt("value"));
    }

    [Theory]
    [InlineData("break")]
    [InlineData("continue")]
    [InlineData("return 1")]
    [InlineData("if false { break }")]
    [InlineData("def duplicate [value value] { return $value }")]
    [InlineData("for $item in [1] { def invalid { break } }")]
    public async Task SemanticError_PreventsEarlierAssignment(string invalid)
    {
        SetVariable("value", new ShellNumber(1));
        var state = await Shell.RunCommandAsync(new(), $"$value = 2; {invalid}", System.Threading.CancellationToken.None);
        Assert.True(state.IsError);
        Assert.Equal(1, GetInt("value"));
    }

    private int GetInt(string name)
    {
        var value = GetVariable(name);
        return (int)Assert.IsType<ShellNumber>(value).Value;
    }

    [Theory]
    [InlineData("if true { return 1 }")]
    [InlineData("while true { if true { return 1 } }")]
    [InlineData("do { if true { return 1 } } while true")]
    [InlineData("loop { if true { return 1 } }")]
    [InlineData("for $item in [1,2] { if true { return 1 } }")]
    public async Task NestedReturn_ExitsFunction(string body)
    {
        var state = await RunScriptAsync($"def probe {{ {body}; return 2 }}; $result = (probe)");
        Assert.False(state.IsError);
        Assert.Equal(1, GetInt("result"));
        Assert.False(state.ReturnFunc);
    }

    [Theory]
    [InlineData("for $item in [1,2] { if true { continue }; $count = $count + 1 }")]
    [InlineData("while $index < 2 { $index = $index + 1; if true { continue }; $count = $count + 1 }")]
    [InlineData("do { $index = $index + 1; if true { continue }; $count = $count + 1 } while $index < 2")]
    [InlineData("loop { $index = $index + 1; if $index > 2 { break }; if true { continue }; $count = $count + 1 }")]
    public async Task NestedContinue_SkipsRemainderOfIteration(string body)
    {
        var state = await RunScriptAsync($"$count = 0; $index = 0; {body}");
        Assert.False(state.IsError);
        Assert.Equal(0, GetInt("count"));
        Assert.False(state.ContinueBlock);
    }

    [Fact]
    public async Task If_TrueCondition_ExecutesThenBranch()
    {
        var state = await RunScriptAsync("$x = 0\nif 1 < 2 { $x = 10 } else { $x = 20 }");
        Assert.False(state.IsError);
        Assert.Equal(10, GetInt("x"));
    }

    [Theory]
    [InlineData("dir \"*.missing-regression-file\" --directory .")]
    [InlineData("$files = (dir \"*.missing-regression-file\" --directory .)")]
    public async Task ReusedCommand_BindsOptionsWithoutMutatingAst(string command)
    {
        var state = await RunScriptAsync($"def probe {{ {command} }}; probe; probe");
        Assert.False(state.IsError);
    }

    [Fact]
    public async Task If_FalseCondition_ExecutesElseBranch()
    {
        var state = await RunScriptAsync("$x = 0\nif 1 > 2 { $x = 10 } else { $x = 20 }");
        Assert.False(state.IsError);
        Assert.Equal(20, GetInt("x"));
    }

    [Fact]
    public async Task If_FalseCondition_NoElse_DoesNothing()
    {
        var state = await RunScriptAsync("$x = 5\nif false { $x = 99 }");
        Assert.False(state.IsError);
        Assert.Equal(5, GetInt("x"));
    }

    [Fact]
    public async Task While_Counts_UntilConditionFalse()
    {
        var state = await RunScriptAsync("$i = 0\nwhile $i < 3 { $i = ($i + 1) }");
        Assert.False(state.IsError);
        Assert.Equal(3, GetInt("i"));
    }

    [Fact]
    public async Task While_Break_ExitsEarly()
    {
        var state = await RunScriptAsync("$i = 0\nwhile $i < 100 { $i = ($i + 1)\nif $i == 5 break }");
        Assert.False(state.IsError);
        Assert.Equal(5, GetInt("i"));
    }

    [Fact]
    public async Task While_Continue_SkipsRemainderOfBody()
    {
        var script = "$i = 0\n$count = 0\nwhile $i < 5 { $i = ($i + 1)\nif $i == 3 continue\n$count = ($count + 1) }";
        var state = await RunScriptAsync(script);
        Assert.False(state.IsError);
        Assert.Equal(5, GetInt("i"));
        Assert.Equal(4, GetInt("count"));
    }

    [Fact]
    public async Task DoWhile_ExecutesBodyAtLeastOnce_WhenConditionFalse()
    {
        var state = await RunScriptAsync("$x = 0\ndo { $x = ($x + 1) } while $x < 0");
        Assert.False(state.IsError);
        Assert.Equal(1, GetInt("x"));
    }

    [Fact]
    public async Task For_OverArray_AccumulatesValues()
    {
        var state = await RunScriptAsync("$sum = 0\nfor $i in [1, 2, 3, 4] { $sum = ($sum + $i) }");
        Assert.False(state.IsError);
        Assert.Equal(10, GetInt("sum"));
    }

    [Fact]
    public async Task For_Break_StopsIteration()
    {
        var state = await RunScriptAsync("$sum = 0\nfor $i in [1, 2, 3, 4] { if $i == 3 break\n$sum = ($sum + $i) }");
        Assert.False(state.IsError);
        Assert.Equal(3, GetInt("sum"));
    }

    [Fact]
    public async Task For_Continue_SkipsSelectedIterations()
    {
        var state = await RunScriptAsync("$sum = 0\nfor $i in [1, 2, 3, 4] { if $i == 2 continue\n$sum = ($sum + $i) }");
        Assert.False(state.IsError);
        Assert.Equal(8, GetInt("sum"));
    }

    [Fact]
    public async Task Loop_Break_TerminatesWithCounter()
    {
        var state = await RunScriptAsync("$i = 0\nloop { $i = ($i + 1)\nif $i == 7 break }");
        Assert.False(state.IsError);
        Assert.Equal(7, GetInt("i"));
    }

    [Fact]
    public async Task NestedControlFlow_ComputesExpected()
    {
        var script = "$total = 0\nfor $i in [1, 2, 3] { $j = 0\nwhile $j < $i { $total = ($total + 1)\n$j = ($j + 1) } }";
        var state = await RunScriptAsync(script);
        Assert.False(state.IsError);
        Assert.Equal(6, GetInt("total"));
    }

    [Fact]
    public async Task Block_ExecutesStatementsSequentially()
    {
        var state = await RunScriptAsync("{ $a = 1\n$b = ($a + 1)\n$c = ($b + 1) }");
        Assert.False(state.IsError);
        Assert.Equal(1, GetInt("a"));
        Assert.Equal(2, GetInt("b"));
        Assert.Equal(3, GetInt("c"));
    }

    [Fact]
    public async Task Block_PrintFailure_ReturnsError()
    {
        Shell.StdOutRedirect = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "out.txt");

        var state = await RunScriptAsync("{ echo value }");

        Assert.True(state.IsError);
    }

    [Fact]
    public async Task Pipe_PrintFailure_ReturnsError()
    {
        Shell.StdOutRedirect = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "out.txt");

        var state = await RunScriptAsync("echo value | echo");

        Assert.True(state.IsError);
    }

    [Fact]
    public async Task Assignment_ChainedArithmetic_ComputesExpected()
    {
        var state = await RunScriptAsync("$x = ((2 + 3) * 4 - 1)");
        Assert.False(state.IsError);
        Assert.Equal(19, GetInt("x"));
    }

    [Theory]
    [InlineData("+=", 9)]
    [InlineData("-=", 3)]
    [InlineData("*=", 18)]
    [InlineData("/=", 2)]
    public async Task CompoundAssignment_UsesArithmeticRules(string assignment, int expected)
    {
        var script = $"$value = 6; $value {assignment} 3";
        var parser = new StatementParser(script);
        var statements = parser.ParseStatements();
        Assert.False(parser.Errors.HasErrors);
        Assert.Equal($"$value {assignment} 3", statements[1].ToString());
        var state = await RunScriptAsync(script);
        Assert.False(state.IsError);
        Assert.Equal(expected, GetInt("value"));
    }

    [Fact]
    public async Task CompoundAssignment_InFunction_RemainsLocal()
    {
        var state = await RunScriptAsync("$value = 1; def increment { $value += 2; return $value }; $local = (increment)");
        Assert.False(state.IsError);
        Assert.Equal(1, GetInt("value"));
        Assert.Equal(3, GetInt("local"));
    }

    [Theory]
    [InlineData("while true {}")]
    [InlineData("do {} while true")]
    [InlineData("loop {}")]
    [InlineData("for $value in [1] {}")]
    public async Task PureLoop_ObservesCancellation(string script)
    {
        var statement = Assert.Single(new StatementParser(script).ParseStatements());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => statement.RunAsync(Shell, new(), new System.Threading.CancellationToken(true)));
    }

    [Fact]
    public async Task For_OverStrings_BindsTextElements()
    {
        var state = await RunScriptAsync("for $x in [\"a\", \"b\"] { }");
        Assert.False(state.IsError);
        Assert.Equal("b", Assert.IsType<ShellText>(GetVariable("x")).Text);
    }

    [Fact]
    public async Task For_OverBooleans_BindsBoolElements()
    {
        var state = await RunScriptAsync("for $x in [true, false] { }");
        Assert.False(state.IsError);
        Assert.False(Assert.IsType<ShellBool>(GetVariable("x")).Value);
    }

    [Fact]
    public async Task For_OverNull_PreservesNullThroughFunctionAndArray()
    {
        var state = await RunScriptAsync("def identity [value] { return $value }; for $x in [null] { $result = [(identity $x)] }");
        Assert.False(state.IsError);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, Assert.IsType<ShellJson>(GetVariable("x")).Value.ValueKind);
        Assert.Equal("[null]", Assert.IsType<ShellJson>(GetVariable("result")).Value.GetRawText());
    }

    [Theory]
    [InlineData("1.5", true)]
    [InlineData("-0.5", true)]
    [InlineData("2147483648.0", true)]
    [InlineData("0.0", false)]
    public async Task NumericTruthiness_IsIndependentOfValueOrigin(string number, bool expected)
    {
        var state = await RunScriptAsync($"$direct = false; if {number} {{ $direct = true }}; $obj = {{value: {number}}}; $json = false; if $obj.value {{ $json = true }}; def truth [value] {{ if $value {{ return true }}; return false }}; $function = (truth $obj.value); for $item in [{number}] {{ $loop = (truth $item) }}");
        Assert.False(state.IsError);
        foreach (var name in new[] { "direct", "json", "function", "loop" })
        {
            Assert.Equal(expected, Assert.IsType<ShellBool>(GetVariable(name)).Value);
        }
    }

    [Fact]
    public async Task For_OverObjects_BindsJsonElements()
    {
        var state = await RunScriptAsync("for $x in [{ id: 1 }] { }");
        Assert.False(state.IsError);
        Assert.IsType<ShellJson>(GetVariable("x"));
    }

    [Fact]
    public async Task For_OverNestedArrays_BindsJsonElements()
    {
        var state = await RunScriptAsync("for $x in [[1, 2], [3, 4]] { }");
        Assert.False(state.IsError);
        Assert.IsType<ShellJson>(GetVariable("x"));
    }

    [Fact]
    public async Task For_OverNonArray_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunScriptAsync("for $x in 5 { }"));
    }

    [Theory]
    [InlineData("[1.5,2.5]", 4.0)]
    [InlineData("[2147483648.0,1]", 2147483649.0)]
    public async Task For_OverDecimalAndLargeNumbers_PreservesValues(string values, double expected)
    {
        var state = await RunScriptAsync($"$sum = 0; for $value in {values} {{ $sum = $sum + $value }}");
        Assert.False(state.IsError);
        Assert.Equal(expected, Assert.IsType<ShellDecimal>(GetVariable("sum")).Value);
    }
}
