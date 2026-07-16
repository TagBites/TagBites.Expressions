using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class ParsingTests : ExpressionTestBase
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1 + * 2")]
    [InlineData("list[]")]
    [InlineData("list[")]
    [InlineData("1 + (")]
    [InlineData("{ 1 }")]
    [InlineData("( 1")]
    [InlineData("list.Func_abs()")]
    [InlineData("list.Min(")]
    [InlineData("list.Min(x=>y)")]
    [InlineData("abc")]
    [InlineData("v + abs")]
    [InlineData("1 switch { \"a\" => 2, _ => 0 }")]
    [InlineData("1 switch { 1 => \"a\", _ => 0 }")]
    [InlineData("1 switch { 1 => 'a', _ => \"a\" }")]
    [InlineData("2d == 2m")]
    [InlineData("2d + 2m")]
    [InlineData("new()")]
    [InlineData(@"""a"" < ""b""")]
    [InlineData(@"""a"" <= ""b""")]
    [InlineData(@"""a"" > ""b""")]
    [InlineData(@"""a"" >= ""b""")]
    public void InvalidSyntax(string? script)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(TestModel), "this"),
                (typeof(TestModel), "m"),
                (typeof(IList<int>), "list"),
                (typeof(int), "v")
            },
            UseFirstParameterAsThis = true,
        };

        Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script!, options));
    }

    [Theory]
    [InlineData("1..2")]
    [InlineData("arr[1..^1]")]
    public void UnsupportedButValidCSharpSyntax(string script)
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int[]), "arr") } };

        var ex = Assert.Throws<ExpressionParserException>(() => ExpressionParser.Parse(script, options));
        Assert.Contains("Unsupported expression", ex.Message);
    }

    [Theory]
    [InlineData("1 switch { 1 => new [] { 1, 2, 3 }.Select(x => (x, x + 1).Item2).Sum() }", 9)]
    public void ComplexExpressions(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);
}
