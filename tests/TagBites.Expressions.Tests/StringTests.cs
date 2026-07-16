namespace TagBites.Expressions.Tests;

public class StringTests : ExpressionTestBase
{
    [Theory]
    [InlineData(@"""a"" + ""b""", "ab")]
    [InlineData(@"""a"" == ""a""", true)]
    [InlineData(@"""a"" == ""b""", false)]
    [InlineData(@"""a"" != ""a""", false)]
    [InlineData(@"""b"" != ""a""", true)]
    [InlineData(@"""a"" + 1", "a1")]
    [InlineData(@"1 + ""a""", "1a")]
    [InlineData(@"'b' + ""a""", "ba")]
    [InlineData(@"(1 == 2 ? 1 : null) + ""a""", "a")]
    [InlineData(@"(1 == 1 ? 1 : null) + ""a""", "1a")]
    [InlineData("$\"{\"a\"}.{\"b\"}\"", "a.b")]
    [InlineData("$\"{1.23:0}x{2.34:00}\"", "1x02")]
    public void StringOperators(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData(@"""a"" < ""b""", true)]
    [InlineData(@"""b"" < ""a""", false)]
    [InlineData(@"""a"" <= ""b""", true)]
    [InlineData(@"""b"" <= ""a""", false)]
    [InlineData(@"""a"" > ""b""", false)]
    [InlineData(@"""b"" > ""a""", true)]
    [InlineData(@"""a"" >= ""b""", false)]
    [InlineData(@"""b"" >= ""a""", true)]
    public void StringRelationalOperators_WhenAllowed(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions { AllowStringRelationalOperators = true };
        ExecuteAndTest(script, options, expectedResult);
    }

    [Theory]
    [InlineData("$\"{1 + 2}\"", "3")]
    [InlineData("$\"sum = {1 + 2}!\"", "sum = 3!")]
    [InlineData("$\"{1}-{2}-{3}\"", "1-2-3")]
    [InlineData("$\"{true} {false}\"", "True False")]
    [InlineData("$\"{(1 < 2 ? \"y\" : \"n\")}\"", "y")]
    [InlineData("$\"{5,4}\"", "   5")]
    [InlineData("$\"{5,-4}!\"", "5   !")]
    [InlineData("$\"{5,6:000}\"", "   005")]
    [InlineData("$\"{255:X}\"", "FF")]
    [InlineData("$\"{255:x4}\"", "00ff")]
    [InlineData("$\"{{literal {1 + 1}}}\"", "{literal 2}")]
    [InlineData("$\"{new DateTime(2021, 8, 14):yyyy-MM-dd}\"", "2021-08-14")]
    [InlineData("$\"{new DateTime(2021, 8, 14),12:yyyy-MM-dd}\"", "  2021-08-14")]
    public void StringInterpolation(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);
}
