using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class DefaultTests : ExpressionTestBase
{
    [Theory]
    [InlineData("default(int)", 0)]
    [InlineData("default(int?) == null", true)]
    [InlineData("default(string) == null", true)]
    [InlineData("default(bool)", false)]
    [InlineData("default(DateTime).Year", 1)]
    [InlineData("default(int[]) == null", true)]
    public void DefaultExpression(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("m.Sum(default, 5)", 5)]
    [InlineData("m.Sum(default, default)", 0)]
    [InlineData("m.Echo(default)", "<null>")]
    [InlineData("v == default", false)]
    [InlineData("(v - 7) == default", true)]
    [InlineData("m.ChildNull ?? default", null)]
    [InlineData("(1 > 0) ? default : m.Echo(\"x\")", null)]
    [InlineData("(int)default", 0)]
    [InlineData("(string)default == null", true)]
    public void BareDefaultLiteral(string script, object? expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(int), "v"),
                (typeof(TestModel), "m")
            }
        };

        ExecuteAndTest(script, options, expectedResult, 7, new TestModel());
    }

    [Theory]
    [InlineData("default")]
    [InlineData("default == default")]
    [InlineData("(1 > 0) ? default : default")]
    [InlineData("m.Overloaded(default)")]
    [InlineData("m.ReturnArgument(default)")]
    public void BareDefaultWithoutTargetTypeFails(string script) =>
        Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script, new ExpressionParserOptions
        {
            Parameters = { (typeof(TestModel), "m") }
        }));
}
