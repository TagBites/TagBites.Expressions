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
    [InlineData("true ? 1 : 2L", 1L)]
    [InlineData("true ? (byte)1 : 2", 1)]
    [InlineData("true ? 4 : (long?)5", 4L)]
    [InlineData("true ? (int?)4 : 5", 4)]
    [InlineData("false ? null : \"a\"", "a")]
    public void TernaryOperator(string script, object? expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("(object)\"s\" == \"s\"", true)]
    [InlineData("((object)1) == ((object)1)", false)]
    [InlineData("(object)null == null", true)]
    [InlineData("(int?)4 ?? 2.5", 4.0)]
    [InlineData("(int?)null ?? 5L", 5L)]
    [InlineData("(long?)5L ?? (int?)7", 5L)]
    [InlineData("(long?)null ?? (int?)7", 7L)]
    [InlineData("(double?)null ?? (uint?)3u", 3.0)]
    [InlineData("(int?)4 ?? null", 4)]
    [InlineData("(int?)null ?? null", null)]
    [InlineData("null ?? \"pending\"", "pending")]
    [InlineData("1 > 0 ? \"active\" : null", "active")]
    [InlineData("(bool?)true & (bool?)true", true)]
    [InlineData("(bool?)null | (bool?)true", true)]
    public void ReferenceEqualityAndCoalescing(string script, object? expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("(object)5 == 5")]
    [InlineData("(object)5 != (byte)3")]
    [InlineData("5 == (object)5")]
    [InlineData("(object)true == true")]
    [InlineData("(object)5 == (int?)5")]
    [InlineData("(bool?)true && true")]
    [InlineData("true && (bool?)true")]
    [InlineData("false || (bool?)null")]
    [InlineData("(int?)4 ?? (uint?)3u")]
    [InlineData("(uint?)7u ?? (int?)7")]
    [InlineData("null ?? null")]
    public void InvalidOperatorOperands_Throws(string script) => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script));

    [Theory]
    [InlineData("true ? (int?)4 : 5L")]
    [InlineData("true ? 2.5 : (int?)4")]
    [InlineData("true ? 2.5m : (int?)4")]
    [InlineData("true ? (sbyte)-2 : 5u")]
    [InlineData("true ? 5u : -4")]
    [InlineData("1 switch { 1 => (int?)4, _ => 5L }")]
    [InlineData("1 > 0 ? null : null")]
    public void TernaryWithoutCommonOperandType_Throws(string script) => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script));

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
    [InlineData("5ul + 5", 10ul)]
    [InlineData("5 + 5ul", 10ul)]
    [InlineData("10ul * 2", 20ul)]
    [InlineData("7ul % 2", 1ul)]
    [InlineData("ulong.MaxValue - 1", ulong.MaxValue - 1)]
    [InlineData("uint.MaxValue / 2", uint.MaxValue / 2)]
    [InlineData("3u - 1", 2u)]
    [InlineData("1ul < 2", true)]
    [InlineData("ulong.MaxValue > 100", true)]
    [InlineData("3u + (-1)", 2L)]
    [InlineData("(byte)3 + 5ul", 8ul)]
    [InlineData("'B' & 5ul", 0ul)]
    [InlineData("'B' + 5ul", 71ul)]
    [InlineData("5L + 5ul", 10ul)]
    [InlineData("5ul == 5L", true)]
    [InlineData("true ? 5L : 5ul", 5ul)]
    [InlineData("5u + (byte)3", 8u)]
    [InlineData("(byte)3 & 5u", 1u)]
    [InlineData("'B' + 5u", 71u)]
    [InlineData("(short)7 + 5u", 12L)]
    [InlineData("5u + (int?)4", 9L)]
    [InlineData("(int?)4 * 5u", 20L)]
    [InlineData("-(5u)", -5L)]
    [InlineData("-(uint?)5", -5L)]
    public void UnsignedConstantPromotion(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

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
    [InlineData("~TypeCode.Boolean", ~TypeCode.Boolean)]
    [InlineData("(int)~DayOfWeek.Monday", -2)]
    [InlineData("~(TypeCode?)TypeCode.Boolean", ~TypeCode.Boolean)]
    [InlineData("(TypeCode.Boolean | TypeCode.Char) & ~TypeCode.Char", TypeCode.Boolean)]
    [InlineData("TypeCode.Empty == 0", true)]
    [InlineData("TypeCode.Boolean != 0", true)]
    [InlineData("((TypeCode?)TypeCode.Boolean) == TypeCode.Boolean", true)]
    [InlineData("TypeCode.Boolean == ((TypeCode?)TypeCode.Boolean)", true)]
    [InlineData("((TypeCode?)null) == TypeCode.Boolean", false)]
    [InlineData("((TypeCode?)null) != TypeCode.Boolean", true)]
    [InlineData("((TypeCode?)TypeCode.Char) > TypeCode.Boolean", true)]
    [InlineData("((TypeCode?)null) > TypeCode.Boolean", false)]
    [InlineData("((TypeCode?)TypeCode.Char) - TypeCode.Boolean", TypeCode.Char - TypeCode.Boolean)]
    [InlineData("1 - TypeCode.Boolean", 1 - TypeCode.Boolean)]
    [InlineData("1 - ((TypeCode?)TypeCode.Boolean)", 1 - TypeCode.Boolean)]
    [InlineData("(1 - ((TypeCode?)null)) == null", true)]
    [InlineData("1L - TypeCode.Boolean", null)]
    [InlineData("TypeCode.Boolean * 1", null)]
    [InlineData("TypeCode.Boolean == 3", null)]
    [InlineData("TypeCode.Boolean == DayOfWeek.Monday", null)]
    [InlineData("((TypeCode?)TypeCode.Boolean) + 1", TypeCode.Char)]
    [InlineData("1 + ((TypeCode?)TypeCode.Boolean)", TypeCode.Char)]
    [InlineData("((TypeCode?)TypeCode.Char) - 1", TypeCode.Boolean)]
    [InlineData("(((TypeCode?)TypeCode.Char) - 1).ToString()", "Boolean")]
    [InlineData("((TypeCode?)TypeCode.Char) - TypeCode.Boolean == 1", true)]
    [InlineData("((TypeCode?)null) + 1 == null", true)]
    [InlineData("((TypeCode?)TypeCode.Boolean) == 0", false)]
    [InlineData("((TypeCode?)TypeCode.Empty) == 0", true)]
    [InlineData("((TypeCode?)TypeCode.Boolean) == null", false)]
    [InlineData("((TypeCode?)TypeCode.Boolean) == 3", null)]
    [InlineData("((TypeCode?)TypeCode.Boolean) < 3", null)]
    [InlineData("((TypeCode?)TypeCode.Boolean) & 3", null)]
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
