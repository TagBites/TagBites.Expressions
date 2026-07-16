namespace TagBites.Expressions.Tests;

public class OperatorTests : ExpressionTestBase
{
    [Theory]
    [InlineData("1 + 2", 3)]
    [InlineData("1 + +2", 3)]
    [InlineData("1 - 2", -1)]
    [InlineData("1 - -2", 3)]
    [InlineData("1 * 2", 2)]
    [InlineData("4 / 2", 2)]
    [InlineData("1d / 2d", 0.5)]
    [InlineData("1.5d * 2d", 3d)]
    [InlineData("5 % 2", 1)]
    public void MathOperators(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 << 2", 4)]
    [InlineData("2 >> 1", 1)]
    [InlineData("1 | 2 | 4", 7)]
    [InlineData("7 & 2", 2)]
    [InlineData("7 ^ 2", 5)]
    [InlineData("~5", -6)]
    [InlineData("~0L", -1L)]
    [InlineData("~5 & 7", 2)]
    [InlineData("1L << 40", 1099511627776L)]   // shift on long: result type follows left operand
    [InlineData("(long)1 << 40", 1099511627776L)]
    [InlineData("1 << (byte)2", 4)]            // shift count promoted from byte
    public void BitwiseOperators(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 == 2", false)]
    [InlineData("2 == 2", true)]
    [InlineData("1 != 2", true)]
    [InlineData("2 != 2", false)]
    [InlineData("1 < 2", true)]
    [InlineData("2 < 1", false)]
    [InlineData("1 <= 2", true)]
    [InlineData("2 <= 1", false)]
    [InlineData("1 > 2", false)]
    [InlineData("2 > 1", true)]
    [InlineData("1 >= 2", false)]
    [InlineData("2 >= 1", true)]
    [InlineData("!true", false)]
    [InlineData("!false", true)]
    [InlineData("!(1 == 2)", true)]
    public void LogicalOperators(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 < 2 ? 1 : 2", 1)]
    [InlineData("1 > 2 ? 1 : 2", 2)]
    [InlineData("1 == 2 ? 1 : null", null)]
    [InlineData("1 == 1 ? 1 : null", 1)]
    [InlineData("1 == 2 ? null : 1", 1)]
    [InlineData("1 == 1 ? null : 1", null)]
    public void TernaryOperator(string script, object? expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("~(byte)5", -6)]
    [InlineData("-(byte)5", -5)]
    [InlineData("+(short)5", 5)]
    [InlineData("(byte)200 + (byte)100", 300)]
    [InlineData("(short)5 * (short)3", 15)]
    [InlineData("(byte)1 + (short)2", 3)]
    [InlineData("(char)65 + 1", 66)]
    public void SmallIntegerPromotion(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("a + b", null, null, 5)]
    [InlineData("a + b", 8, 5, 3)]
    [InlineData("a > b", false, null, 5)]
    [InlineData("a > b", false, 5, 5)]
    [InlineData("a > b", true, 5, 3)]
    public void NullableArithmetic_PropagatesRuntimeNull(string script, object? expectedResult, int? a, int? b)
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int?), "a"), (typeof(int?), "b") } };
        ExecuteAndTest(script, options, expectedResult, a, b);
    }

    [Theory]
    [InlineData("checked(1 + 2)", 3)]
    [InlineData("unchecked(1 + 2)", 3)]
    [InlineData("unchecked(2147483647 + 1)", int.MinValue)]
    [InlineData("unchecked((int)(2147483647L + 1))", int.MinValue)]
    public void CheckedContext(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Fact]
    public void CheckedOverflowThrows()
    {
        var ex = Assert.ThrowsAny<Exception>(() => Execute("checked(2147483647 + 1)", null));
        Assert.IsType<OverflowException>(ex.InnerException ?? ex);
    }

    [Fact]
    public void UncheckedNegation_WrapsAtRuntime()
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int), "m") } };
        ExecuteAndTest("unchecked(-m)", options, int.MinValue, int.MinValue);
    }

    [Fact]
    public void NullForgivingOperator()
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(string), "s") }
        };

        ExecuteAndTest("s!.Length", options, 2, "ab");
        ExecuteAndTest("s!.Length + 1", options, 3, "ab");
    }

    [Theory]
    [InlineData("TypeCode.Boolean + 1", TypeCode.Boolean + 1)]
    [InlineData("1 + TypeCode.Boolean", 1 + TypeCode.Boolean)]
    [InlineData("TypeCode.Boolean - 1", TypeCode.Boolean - 1)]
    [InlineData("TypeCode.Char - TypeCode.Boolean", TypeCode.Char - TypeCode.Boolean)]
    [InlineData("TypeCode.Boolean == TypeCode.Char", false)]
    [InlineData("TypeCode.Boolean < TypeCode.Char", true)]
    [InlineData("TypeCode.Boolean & TypeCode.Char", TypeCode.Boolean & TypeCode.Char)]
    [InlineData("TypeCode.Boolean | TypeCode.Char", TypeCode.Boolean | TypeCode.Char)]
    [InlineData("TypeCode.Boolean ^ TypeCode.Char", TypeCode.Boolean ^ TypeCode.Char)]
    [InlineData("TypeCode.Empty == 0", true)]
    [InlineData("TypeCode.Boolean != 0", true)]
    [InlineData("1 - TypeCode.Boolean", null)]
    [InlineData("TypeCode.Boolean * 1", null)]
    [InlineData("TypeCode.Boolean == 3", null)]
    [InlineData("TypeCode.Boolean == DayOfWeek.Monday", null)]
    public void EnumOperators(string script, object? expectedResult)
    {
        var options = new ExpressionParserOptions { IncludedTypes = { typeof(TypeCode), typeof(DayOfWeek) } };

        if (expectedResult is null)
        {
            Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script, options));
            return;
        }

        ExecuteAndTest(script, options, expectedResult);
    }
}
