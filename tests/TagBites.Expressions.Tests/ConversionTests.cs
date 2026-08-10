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

    [Theory]
    [InlineData("((Money)3).Value == 3m", true)]
    [InlineData("((Money)3L).Value == 3m", true)]
    [InlineData("((Money)(byte)3).Value == 3m", true)]
    [InlineData("((Money)3m).Value == 3m", true)]
    [InlineData("(m + 2).Value == 3m", true)]
    public void CustomOperatorAfterStandardConversion(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(Money), "m") },
            IncludedTypes = { typeof(Money) }
        };

        ExecuteAndTest(script, options, expectedResult, new Money(1m));
    }

    [Fact]
    public void MixedTypeOperatorOverload()
    {
        ExecuteAndTest("new DateTime(2021, 8, 14) - TimeSpan.FromDays(1)", new DateTime(2021, 8, 13));
        ExecuteAndTest("new DateTime(2021, 8, 14) + TimeSpan.FromDays(1)", new DateTime(2021, 8, 15));

        ExecuteAndTest("TimeSpan.FromHours(1) * 2", TimeSpan.FromHours(2));
        ExecuteAndTest("TimeSpan.FromHours(1) * 2.5", TimeSpan.FromMinutes(150));
        ExecuteAndTest("TimeSpan.FromHours(3) / 2", TimeSpan.FromMinutes(90));
    }

    [Theory]
    [InlineData("1d + 2m")]
    [InlineData("new DateTime(2021, 8, 14) + 2")]
    [InlineData("(int)true")]
    [InlineData("(byte)true")]
    [InlineData("(double)true")]
    [InlineData("(char)true")]
    [InlineData("(bool)1")]
    [InlineData("(bool)0.5")]
    [InlineData("(DayOfWeek)true")]
    [InlineData("(bool?)1")]
    public void InvalidCastOperator(string script) => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script));

    [Theory]
    [InlineData("(DayOfWeek)2.5m", DayOfWeek.Tuesday)]
    [InlineData("(DayOfWeek)6.9m", DayOfWeek.Saturday)]
    [InlineData("(decimal)DayOfWeek.Friday == 5m", true)]
    [InlineData("(DayOfWeek?)(decimal?)2.5m", DayOfWeek.Tuesday)]
    public void DecimalEnumCast(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("!5")]
    [InlineData("!5ul")]
    [InlineData("!(int?)4")]
    [InlineData("!'a'")]
    [InlineData("!2.5")]
    [InlineData("!DayOfWeek.Monday")]
    public void LogicalNotOnNonBool_Throws(string script) => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script));

    [Theory]
    [InlineData("1u + 1", 2u)]
    [InlineData("2u - 1", 1u)]
    [InlineData("(uint)5 + (int)3", 8L)]
    [InlineData("uint.MaxValue + 1", 0u)]
    [InlineData("(short)1 + 2u", 3L)]
    [InlineData("5u + 3u", 8u)]
    public void UIntPromotion(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

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
