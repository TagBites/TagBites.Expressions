namespace TagBites.Expressions.Tests;

public class ResultTypeTests : ExpressionTestBase
{
    [Theory]
    [InlineData("1", typeof(long), 1L)]
    [InlineData("1", typeof(double), 1d)]
    [InlineData("1", typeof(int?), 1)]
    [InlineData("1", typeof(long?), 1L)]
    [InlineData("1", typeof(object), 1)]
    [InlineData("(int?)1", typeof(int?), 1)]
    public void ImplicitCastForReturnType(string script, Type resultType, object expected)
    {
        var options = new ExpressionParserOptions { ResultType = resultType };
        ExecuteAndTest(script, options, expected);
    }

    [Theory]
    [InlineData("1", typeof(bool))]
    [InlineData("(int?)1", typeof(int))]
    public void InvalidReturnTypeConversion(string script, Type resultType)
    {
        var options = new ExpressionParserOptions { ResultType = resultType };
        Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script, options));
    }

    [Fact]
    public void ReturnTypeChangesReturnType()
    {
        var options = new ExpressionParserOptions { ResultType = typeof(object) };
        Assert.Equal(typeof(object), ExpressionParser.Parse("1 + 2", options).ReturnType);
    }

    [Theory]
    [InlineData("1", typeof(long), 1L)]
    [InlineData("1", typeof(object), 1)]
    [InlineData("2.5", typeof(int), 2)]
    [InlineData("(int?)1", typeof(int), 1)]
    public void CastReturnType(string script, Type castType, object expected)
    {
        var options = new ExpressionParserOptions { ResultCastType = castType };
        ExecuteAndTest(script, options, expected);
    }

    [Fact]
    public void CastReturnTypeChangesReturnType()
    {
        var options = new ExpressionParserOptions { ResultCastType = typeof(object) };
        Assert.Equal(typeof(object), ExpressionParser.Parse("1 + 2", options).ReturnType);
    }
}
