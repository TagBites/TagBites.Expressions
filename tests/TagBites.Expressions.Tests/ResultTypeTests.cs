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

    [Fact]
    public void ForkResultType_ChangesReturnType()
    {
        var options = new ExpressionParserOptions { ResultType = typeof(int) };
        Assert.Equal(typeof(long), ExpressionParser.Parse("1 + 2", options.Fork(resultType: typeof(long))).ReturnType);
    }

    [Fact]
    public void ForkResultType_OnFrozenOptions_ReusedAcrossResultTypes()
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int), "n") } };
        ExpressionParser.Parse("n", options);

        Assert.Equal(typeof(long), ExpressionParser.Parse("n", options.Fork(resultType: typeof(long))).ReturnType);
        Assert.Equal(typeof(object), ExpressionParser.Parse("n", options.Fork(resultType: typeof(object))).ReturnType);
    }

    [Fact]
    public void ForkResultType_InvalidConversion_Throws()
    {
        var options = new ExpressionParserOptions();
        Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse("1", options.Fork(resultType: typeof(bool))));
    }

    [Fact]
    public void ForkResultCastType_ChangesReturnType()
    {
        var options = new ExpressionParserOptions();
        Assert.Equal(typeof(object), ExpressionParser.Parse("1 + 2", options.Fork(resultCastType: typeof(object))).ReturnType);
    }

    [Fact]
    public void ForkResultTypeAndCastType_Combined()
    {
        var options = new ExpressionParserOptions();
        // Positional: resultType, resultCastType
        Assert.Equal(typeof(object), ExpressionParser.Parse("1 + 2", options.Fork(typeof(int), typeof(object))).ReturnType);
    }

    [Fact]
    public void ForkResultType_InheritsResultCastType()
    {
        var options = new ExpressionParserOptions { ResultCastType = typeof(object) };
        // Only resultType is overridden; the cast type is inherited.
        Assert.Equal(typeof(object), ExpressionParser.Parse("1 + 2", options.Fork(typeof(int))).ReturnType);
    }
}
