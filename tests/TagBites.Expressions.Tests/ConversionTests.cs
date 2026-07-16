using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class ConversionTests : ExpressionTestBase
{
    [Theory]
    [InlineData("(double)1", 1d)]
    [InlineData("(int)2.5", 2)]
    [InlineData("(float)2.5", 2.5f)]
    [InlineData("(double)2.5m", 2.5)]
    public void CastOperators(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 + 2.1", 3.1)]
    [InlineData("1 + 2L", 3L)]
    [InlineData("(int?)1 + 2L", 3L)]
    [InlineData("1 + (long?)2L", 3L)]
    [InlineData("(int?)1 + (long?)2L", 3L)]
    [InlineData("2 < 1d", false)]
    [InlineData("2 < 1m", false)]
    [InlineData("2 < 1L", false)]
    [InlineData("(int?)2 < 1L", false)]
    [InlineData("2 == 2m", true)]
    [InlineData("1 / 2d", 0.5)]
    public void ImplicitCast(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Fact]
    public void ImplicitDecimalCast()
    {
        ExecuteAndTest("1 + 2m", 3m);
        ExecuteAndTest("(int?)1 + 2m", 3m);
    }

    [Fact]
    public void ImplicitCastFromCustomOperator()
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(Money), "m") }
        };

        var result = (Money)ExpressionParser.Invoke("m + 2.5m", options, new Money(1m))!;
        Assert.Equal(3.5m, result.Value);
    }

    [Fact]
    public void MixedTypeOperatorOverload()
    {
        ExecuteAndTest("new DateTime(2021, 8, 14) - TimeSpan.FromDays(1)", new DateTime(2021, 8, 13));
        ExecuteAndTest("new DateTime(2021, 8, 14) + TimeSpan.FromDays(1)", new DateTime(2021, 8, 15));
    }

    [Theory]
    [InlineData("1 + (uint)2")]
    [InlineData("1d + 2m")]
    [InlineData("new DateTime(2021, 8, 14) + 2")]
    public void InvalidCastOperator(string script) => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script));

    [Theory]
    [InlineData("Math.Min(2, 2)", 2)]
    [InlineData("Math.Min(2L, 2L)", 2L)]
    [InlineData("Math.Min(2d, 2d)", 2d)]
    [InlineData("Math.Min(2, 2L)", 2L)]
    [InlineData("Math.Min(2L, 2)", 2L)]
    [InlineData("Math.Min(2, 2d)", 2d)]
    [InlineData("Math.Min(2d, 2)", 2d)]
    [InlineData("Math.Min(2d, 2f)", 2d)]
    public void ImplicitCastOnMethodCall(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);
}
