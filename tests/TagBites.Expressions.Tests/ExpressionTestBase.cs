namespace TagBites.Expressions.Tests;

public abstract class ExpressionTestBase
{
    protected static void ExecuteAndTest(string script, object? expectedResult, params object?[] args)
    {
        ExecuteAndTest(script, null, expectedResult, args);
    }
    protected static void ExecuteAndTest(string script, ExpressionParserOptions? options, object? expectedResult, params object?[] args)
    {
        var result = Execute(script, options, args);

        Assert.Equal(expectedResult, result);
    }
    protected static object? Execute(string script, ExpressionParserOptions? options, params object?[] args)
    {
        var expression = ExpressionParser.Parse(script, options);
        var expressionDelegate = expression.Compile();
        return expressionDelegate.DynamicInvoke(args);
    }
}
