using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class LambdaTests : ExpressionTestBase
{
    [Theory]
    [InlineData("list.First()", 1)]
    [InlineData("list.FirstOrDefault()", 1)]
    [InlineData("list.Count()", 3)]
    [InlineData("list.Min()", 1)]
    [InlineData("list.Max()", 3)]
    [InlineData("list.Sum()", 6)]
    [InlineData("list.First(x => x > 2)", 3)]
    [InlineData("list.Where(x => x > 2).Count()", 1)]
    [InlineData("list.Where((x, i) => x > 1 && i > 1).Count()", 1)]
    [InlineData("array.First(x => x > 2)", 3)]
    [InlineData("models.First(x => x.Value > 1).Value", 10)]
    [InlineData("listOfLists.Select(x => x.Select(y => y * 2).Max()).Sum()", 3 * 2 + 6 * 2)]
    [InlineData("listOfLists.Select(x => x.Select(y => y * 2).Select(x => x * 2).Max()).Sum()", 3 * 2 * 2 + 6 * 2 * 2)]
    [InlineData("list.Sum(x => x + n)", 9)]
    public void LambdaAndLinq(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(IList<int>), "list"),
                (typeof(int[]), "array"),
                (typeof(IList<IList<int>>), "listOfLists"),
                (typeof(IList<TestModel>), "models"),
                (typeof(int), "n")
            }
        };
        var args = new object[]
        {
            new List<int> { 1, 2, 3 },
            new [] { 1, 2, 3 },
            new List<IList<int>> { new List<int> { 1, 2, 3 }, new List<int> { 4, 5, 6 } },
            new List<TestModel> { new (), new (10), new (100) },
            1
        };
        ExecuteAndTest(script, options, expectedResult, args);
    }
}
