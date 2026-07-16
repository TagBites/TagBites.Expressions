namespace TagBites.Expressions.Tests;

public class TupleTests : ExpressionTestBase
{
    [Theory]
    [InlineData("(1, 2).Item1", 1)]
    [InlineData("(1, 2).Item2", 2)]
    [InlineData("(n, n + 1, a + a).Item1", 1)]
    [InlineData("(n, n + 1, a + a).Item2", 2)]
    [InlineData("(n, n + 1, a + a).Item3", "aa")]
    [InlineData("(A: 1, B: 2).Item1", 1)]
    [InlineData("(A: 1, B: 2).Item2", 2)]
    public void Tuple(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = {
                (typeof(int), "n"),
                (typeof(string), "a")
            }
        };

        ExecuteAndTest(script, options, expectedResult, 1, "a");
    }

    [Theory]
    [InlineData("(1, 2) == (1, 2)", true)]
    [InlineData("(1, 2) == (1, 3)", false)]
    [InlineData("(1, 2) != (1, 3)", true)]
    [InlineData(@"(""a"", 1) == (""a"", 1)", true)]
    [InlineData("(1, 2, 3) == (1, 2, 3)", true)]
    public void TupleEquality(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);
}
