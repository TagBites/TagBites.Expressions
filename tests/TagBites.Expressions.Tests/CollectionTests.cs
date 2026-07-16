namespace TagBites.Expressions.Tests;

public class CollectionTests : ExpressionTestBase
{
    [Theory]
    [InlineData("new int[] { 1, 2, 3 }[0]", 1)]
    [InlineData("new [] { 1, 2, 3 }[0]", 1)]
    [InlineData("new long[] { 1, 2, 3 }[1]", 2L)]
    public void Array(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("new int[2].Length", 2)]
    [InlineData("(new int[3])[1]", 0)]
    [InlineData("new int[n].Length", 5)]
    public void ArrayWithSize(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int), "n") } };
        ExecuteAndTest(script, options, expectedResult, 5);
    }

    [Theory]
    [InlineData("(new int[2, 3]).Length", 6)]
    [InlineData("(new int[2, 3]).GetLength(1)", 3)]
    [InlineData("(new int[2, 3])[1, 2]", 0)]
    [InlineData("new int[,] { { 1, 2, 3 }, { 4, 5, 6 } }[1, 2]", 6)]
    [InlineData("new int[,] { { 1, 2 }, { 3, 4 } }[0, 1]", 2)]
    [InlineData("new int[2, 2] { { 1, 2 }, { 3, 4 } }[1, 0]", 3)]
    [InlineData("(new int[3, 3, 3])[1, 1, 1]", 0)]
    [InlineData("new int[,,] { { { 1 } }, { { 2 } } }[1, 0, 0]", 2)]
    [InlineData("new long[,] { { 1, 2 }, { 3, 4 } }[1, 1]", 4L)]
    public void MultiDimensionalArray(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("new int[,] { { 1, 2 }, { 3 } }")]
    [InlineData("new int[3][1]")]
    public void InvalidArrayCreation(string script) => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script));

    [Fact]
    public void NegativeArraySize_ThrowsOverflowException()
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int), "n") } };
        var lambda = ExpressionParser.Parse("new int[n]", options);
        var func = (Func<int, int[]>)lambda.Compile();

        Assert.Throws<OverflowException>(() => func(-1));
    }

    [Theory]
    [InlineData("\"abcd\"[^1]", 'd')]
    [InlineData("\"abcd\"[^2]", 'c')]
    [InlineData("new [] { 10, 20, 30 }[^1]", 30)]
    [InlineData("new [] { 10, 20, 30 }[^3]", 10)]
    [InlineData("(new [] { 1, 2, 3, 4 })[^2] + 100", 103)]
    [InlineData("arr[^1]", 7)]
    [InlineData("list[^2]", 2)]
    [InlineData("arr[^n]", 6)]
    public void IndexFromEnd(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(int[]), "arr"), (typeof(IList<int>), "list"), (typeof(int), "n") }
        };

        ExecuteAndTest(script, options, expectedResult, new[] { 5, 6, 7 }, new List<int> { 1, 2, 3 }, 2);
    }

    [Theory]
    [InlineData(@"""abc""[1]", 'b')]
    [InlineData(@"""abcd"".Substring(1)[0]", 'b')]
    [InlineData("list[1]", 2)]
    public void IndexerAccess(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(IList<int>), "list") }
        };

        ExecuteAndTest(script, options, expectedResult, new List<int> { 1, 2, 3 });
    }

    [Theory]
    [InlineData("list[0]", 1)]
    [InlineData("array[0]", 1)]
    [InlineData("list?[0] ?? 0", 1)]
    [InlineData("(list.Count == array.Length ? list : (IList<int>)array)?[0] ?? 0", 1)]
    public void CollectionIndexing(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(IList<int>), "list"),
                (typeof(int[]), "array"),
            }
        };

        var list = new List<int> { 1 };
        ExecuteAndTest(script, options, expectedResult, list, list.ToArray());
    }

    [Theory]
    [InlineData("new Dictionary<string, int>().Count", 0)]
    [InlineData("new HashSet<int>().Count", 0)]
    [InlineData("new List<List<int>>().Count", 0)]
    public void BuiltInCollectionTypes(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("new List<int> { 1, 2, 3 }.Sum()", 6)]
    [InlineData("new HashSet<int> { 1, 2, 2, 3 }.Count", 3)]
    [InlineData("new List<List<int>> { new List<int> { 1, 2 }, new List<int> { 3, 4 } }[1][0]", 3)]
    [InlineData("new List<List<int>> { new() { 1, 2 }, new() { 3, 4 } }[1][0]", 3)]
    [InlineData(@"new Dictionary<string, int> { { ""a"", 1 }, { ""b"", 2 } }[""b""]", 2)]
    public void CollectionInitializer(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);
}
