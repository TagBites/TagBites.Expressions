using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class NamedArgumentTests : ExpressionTestBase
{
    [Theory]
    [InlineData("m.Subtract(a: 10, b: 3)", 7)]
    [InlineData("m.Subtract(b: 3, a: 10)", 7)]
    [InlineData("m.Subtract(10, b: 3)", 7)]
    // A positional argument may follow a named one when the named argument is in its natural position (C# 7.2).
    [InlineData("m.Subtract(a: 10, 3)", 7)]
    [InlineData("""m.Concat3("x")""", "x-!")]
    [InlineData("""m.Concat3("x", c: "z")""", "x-z")]
    [InlineData("""m.Concat3(a: "x", c: "z")""", "x-z")]
    [InlineData("""m.Concat3(c: "z", a: "x", b: "y")""", "xyz")]
    public void NamedArgumentsBindByName(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(TestModel), "m") }
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel());
    }

    [Theory]
    [InlineData("m.ReturnArgumentExactTypeOrDefault<long>(v: 2)", 0L)]
    [InlineData("m.ReturnArgumentExactTypeOrDefault<long>(v: 2, defaultValue: 1)", 1L)]
    [InlineData("m.ReturnArgumentExactTypeOrDefault<long>(defaultValue: 1, v: 2)", 1L)]
    public void NamedArgumentsOnGenericMethods(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(TestModel), "m") }
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel());
    }

    [Theory]
    [InlineData("""p.Describe(prefix: "x")""", "x:none")]
    public void NamedArgumentsWithOverloads(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(ParamsModel), "p") }
        };

        ExecuteAndTest(script, options, expectedResult, new ParamsModel());
    }

    [Theory]
    // Overload resolution must compare each argument against the parameter it binds to by name, not by position:
    // reordered, the int overload is still the better match for two int arguments.
    [InlineData("m.Widen(1, 2)", "int")]
    [InlineData("m.Widen(a: 1, b: 2)", "int")]
    [InlineData("m.Widen(b: 2, a: 1)", "int")]
    public void NamedArgumentsPickBestOverloadAfterReordering(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(TestModel), "m") }
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel());
    }

    [Theory]
    [InlineData("m.GetValueExtension(value: 5)", 5)]
    public void NamedArgumentsOnExtensionMethods(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(TestModel), "m") },
            IncludedTypes = { typeof(TestModelExtensions) }
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel());
    }

    [Theory]
    [InlineData("m.Subtract(a: 10, x: 3)")]     // unknown parameter name
    [InlineData("m.Subtract(a: 10, a: 3)")]     // duplicate name
    [InlineData("m.Subtract(b: 3, 10)")]        // positional after an out-of-position named argument
    [InlineData("m.Subtract(a: 10)")]           // required parameter left unfilled
    public void InvalidNamedArgumentsFailToBind(string script)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(TestModel), "m") }
        };

        Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script, options));
    }

    [Fact]
    public void NamedArgumentsRespectIgnoreCase()
    {
        var options = new ExpressionParserOptions
        {
            IgnoreCase = true,
            Parameters = { (typeof(TestModel), "m") }
        };

        ExecuteAndTest("m.Subtract(A: 10, B: 3)", options, 7, new TestModel());
    }

    [Fact]
    public void NamesAreCaseSensitiveByDefault()
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(TestModel), "m") }
        };

        Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse("m.Subtract(A: 10, B: 3)", options));
    }

    [Theory]
    // Reordering, mixing, and skipping the optional parameter on constructors.
    [InlineData("new NamedArgModel(1, 2).Signature", "1,2,100")]
    [InlineData("new NamedArgModel(b: 2, a: 1).Signature", "1,2,100")]
    [InlineData("new NamedArgModel(1, c: 3, b: 2).Signature", "1,2,3")]
    [InlineData("new NamedArgModel(c: 3, a: 1, b: 2).Signature", "1,2,3")]
    // Names select the correct constructor overload.
    [InlineData(@"new NamedArgModel(text: ""x"").Signature", "text:x")]
    public void NamedArgumentsOnConstructors(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            IncludedTypes = { typeof(NamedArgModel) }
        };

        ExecuteAndTest(script, options, expectedResult);
    }

    [Theory]
    [InlineData("m[1, 2]", 12)]
    [InlineData("m[row: 1, col: 2]", 12)]
    [InlineData("m[col: 2, row: 1]", 12)]
    public void NamedArgumentsOnIndexers(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(NamedArgModel), "m") }
        };

        ExecuteAndTest(script, options, expectedResult, CreateModel());

        static NamedArgModel CreateModel() => new(0, 0);
    }

    [Theory]
    [InlineData(@"new NamedArgModel(a: 1, z: 2)")]    // unknown constructor parameter name
    [InlineData("m[row: 1, row: 2]")]                 // duplicate indexer argument name
    public void InvalidNamedArgumentsOnConstructorsAndIndexersFail(string script)
    {
        var options = new ExpressionParserOptions
        {
            IncludedTypes = { typeof(NamedArgModel) },
            Parameters = { (typeof(NamedArgModel), "m") }
        };

        Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script, options));
    }
}
