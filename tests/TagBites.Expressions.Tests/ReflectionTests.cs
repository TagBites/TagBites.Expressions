namespace TagBites.Expressions.Tests;

public class ReflectionTests : ExpressionTestBase
{
    [Theory]
    [InlineData("2.GetType().Name", "Int32", false)]
    [InlineData("2.GetType().IsValueType", true, false)]
    [InlineData("2.GetType().IsAssignableFrom(2.GetType())", true, false)]
    [InlineData("2.GetType().FullName", null, false)]
    [InlineData("2.GetType().Assembly", null, false)]
    [InlineData(@"""a"".GetType().GetProperties().Length > 0", null, false)]
    [InlineData("2.GetType().FullName", "System.Int32", true)]
    [InlineData(@"""a"".GetType().GetProperties().Length > 0", true, true)]
    public void ReflectionCall(string script, object? expectedResult, bool allowReflection)
    {
        var options = new ExpressionParserOptions { AllowReflection = allowReflection };
        var shouldParse = expectedResult is not null;

        Assert.Equal(shouldParse, ExpressionParser.TryParse(script, options, out var expression, out _));

        if (expectedResult != null)
        {
            var result = expression!.Compile().DynamicInvoke();
            Assert.Equal(expectedResult, result);
        }
    }
}
