using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class MethodCallTests : ExpressionTestBase
{
    [Theory]
    [InlineData(@"new DateTime(2021, 8, 14).ToString(""yyyy"")", "2021")]
    [InlineData(@"new DateTime(2021, 8, 14).ToString(""yyyy"").Length", 4)]
    public void MethodInvocation(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("v", 10)]
    [InlineData("v + v", 20)]
    [InlineData(@"v.ToString() + ""-"" + t", "10-ten")]
    [InlineData("int.Parse(v.ToString().Substring(0,1))", 1)]
    [InlineData("m.ChildTimesTen.Value + m.Value", 11)]
    public void MethodArguments(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(int), "v"),
                (typeof(string), "t"),
                (typeof(TestModel), "m")
            }
        };

        ExecuteAndTest(script, options, expectedResult, 10, "ten", new TestModel());
    }

    [Theory]
    [InlineData(@"string.Format(""{0} {1}"", 1, 2)", "1 2")]
    [InlineData(@"string.Format(""{0}"", 1)", "1")]
    [InlineData(@"string.Format(""{0} {1} {2}"", 1, 2, ""a"")", "1 2 a")]
    [InlineData(@"string.Concat(""a"", ""b"", 1 + 2, ""d"")", "ab3d")]
    public void ParamsBclMethods(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("p.Sum(1, 2, 3)", 6)]
    [InlineData("p.Sum(1)", 1)]
    [InlineData("p.Sum()", 0)]
    [InlineData("p.SumLong(1, 2, 3)", 6L)]
    [InlineData("p.SumLong(1, 2L)", 3L)]
    [InlineData("p.Sum(new [] { 1, 2, 3 })", 6)]
    [InlineData("p.First(new [] { 5, 6 })", 5)]
    [InlineData("ParamsModel.Join(\"-\", \"a\", \"b\")", "a-b")]
    public void ParamsMethods(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            IncludedTypes = { typeof(ParamsModel) },
            Parameters = { (typeof(ParamsModel), "p") }
        };

        ExecuteAndTest(script, options, expectedResult, new ParamsModel());
    }

    [Theory]
    [InlineData(@"p.Describe(""x"")", "x:none")]
    [InlineData(@"p.Describe(""x"", 1)", "x:1")]
    [InlineData(@"p.Describe(""x"", 1, 2)", "x:2")]
    public void ParamsOverloadResolution(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(ParamsModel), "p") }
        };

        ExecuteAndTest(script, options, expectedResult, new ParamsModel());
    }

    [Theory]
    [InlineData("m.ReturnArgumentExactTypeOrNull<int>(2)", 2)]
    [InlineData("m.ReturnArgumentExactTypeOrNull<int>((object)2)", 2)]
    [InlineData("m.ReturnArgumentExactTypeOrNull<long>(2)", 0L)]
    [InlineData("m.ReturnArgumentExactTypeOrDefault<long>(2)", 0L)]
    [InlineData("m.ReturnArgumentExactTypeOrDefault<long>(2, 1)", 1L)]
    [InlineData("m.ReturnArgumentExactTypeOrDefault<long>(2L)", 2L)]
    [InlineData("m.ReturnArgument(2)", 2)]
    [InlineData("m.ReturnArgument<long>(2)", 2L)]
    public void GenericMethods(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(TestModel), "m") }
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel());
    }

    [Theory]
    [InlineData("m.GetValueExtension(2)", 2)]
    [InlineData("m.GetValueUsingInterfaceExtension(2)", 2)]
    public void ExtensionMethods(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(TestModel), "m") },
            IncludedTypes = { typeof(TestModelExtensions) }
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel());
    }

    [Theory]
    [InlineData("Math.Min(2d, 2m)")]
    [InlineData("Math.Min(2m, 2d)")]
    public void AmbiguousMethodCall(string script) => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script, null));
}
