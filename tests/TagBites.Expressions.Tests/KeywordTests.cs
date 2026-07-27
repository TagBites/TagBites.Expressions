using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class KeywordTests : ExpressionTestBase
{
    [Theory]
    [InlineData("typeof(int) == typeof(int)", true)]
    [InlineData("typeof(int) == typeof(long)", false)]
    [InlineData("typeof(string) != typeof(int)", true)]
    [InlineData("typeof(int?) == typeof(int?)", true)]
    [InlineData("typeof(int[]) == typeof(int[])", true)]
    [InlineData("typeof(int[]) != typeof(int[,])", true)]
    [InlineData("(int[])null == null", true)]
    public void TypeOfExpression(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("nameof(System)", "System")]
    [InlineData("nameof(v)", "v")]
    [InlineData("nameof(m.ChildTimesTen)", "ChildTimesTen")]
    [InlineData("nameof(m.ChildTimesTen.Value)", "Value")]
    public void NameOfExpression(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(int), "v"),
                (typeof(TestModel), "m")
            }
        };

        ExecuteAndTest(script, options, expectedResult, 1, new TestModel());
    }

    [Theory]
    [InlineData("sizeof(bool)", 1)]
    [InlineData("sizeof(byte)", 1)]
    [InlineData("sizeof(char)", 2)]
    [InlineData("sizeof(int)", 4)]
    [InlineData("sizeof(long)", 8)]
    [InlineData("sizeof(double)", 8)]
    [InlineData("sizeof(decimal)", 16)]
    public void SizeOfExpression(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("true ? 1 : throw null", 1)]
    [InlineData("\"x\" ?? throw null", "x")]
    [InlineData("true ? 1 : throw new InvalidOperationException(\"boom\")", 1)]
    public void ThrowExpression(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions { AllowThrowExpressions = true, IncludedTypes = { typeof(InvalidOperationException) } };
        ExecuteAndTest(script, options, expectedResult);
    }

    [Fact]
    public void ThrowExpression_Throws()
    {
        var options = new ExpressionParserOptions { AllowThrowExpressions = true, IncludedTypes = { typeof(InvalidOperationException) } };
        var func = ExpressionParser.Compile<Func<object>>("false ? 1 : throw new InvalidOperationException(\"boom\")", options.Fork(resultCastType: typeof(object)));

        Assert.Throws<InvalidOperationException>(() => func());
    }

    [Theory]
    [InlineData("true ? 1 : throw null")]
    [InlineData("\"x\" ?? throw null")]
    public void ThrowExpression_NotAllowedByDefault(string script)
    {
        Assert.Throws<ExpressionParserException>(() => ExpressionParser.Parse(script));
    }

    [Theory]
    [InlineData("throw null")]
    [InlineData("true ? 1 : throw 5")]
    public void ThrowExpression_Invalid(string script)
    {
        var options = new ExpressionParserOptions { AllowThrowExpressions = true };
        Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script, options));
    }
}
