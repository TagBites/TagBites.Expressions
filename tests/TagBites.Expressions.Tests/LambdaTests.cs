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
    [InlineData("list.Max(x => x * 2)", 6)]
    [InlineData("list.Min(x => x * 2)", 2)]
    [InlineData("models.Max(x => x.Value)", 100)]
    [InlineData("list.Aggregate(0, (a, b) => a + b, r => r * 2)", 12)]
    [InlineData("list.Aggregate(1, (a, b) => a * b, r => r + 100)", 106)]
    [InlineData("list.GroupJoin(array, x => x, y => y, (x, g) => g.Count()).Sum()", 3)]
    [InlineData("list.GroupJoin(new[] { 2, 2, 3 }, o => o, i => i, (o, g) => o * 10 + g.Count()).Sum()", 63)]
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

    [Theory]
    [InlineData("\"hello\".Count()", 5)]
    [InlineData("\"hello\".Reverse().Count()", 5)]
    [InlineData("\"hello\".Where(c => c == 'l').Count()", 2)]
    [InlineData("\"hello\".Select(c => (int)c).Sum()", 532)]
    public void LinqOverString(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("new[] { DayOfWeek.Monday, DayOfWeek.Friday }.Max()", DayOfWeek.Friday)]
    [InlineData("new[] { DayOfWeek.Tuesday, DayOfWeek.Sunday }.Min()", DayOfWeek.Sunday)]
    [InlineData("new[] { DayOfWeek.Monday, DayOfWeek.Friday }.Max() == DayOfWeek.Friday", true)]
    public void EnumSequenceAggregatesKeepEnumType(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("new List<int> { 1, 2, 3 }.Find(x => x > 1)", 2)]
    [InlineData("new List<int> { 1, 2, 3 }.FindIndex(x => x == 2)", 1)]
    [InlineData("new List<int> { 1, 2, 3 }.Exists(x => x == 2)", true)]
    [InlineData("new List<int> { 1, 2, 3 }.TrueForAll(x => x > 0)", true)]
    [InlineData("new List<int> { 3, 1, 2 }.ConvertAll(x => x * 2).Sum()", 12)]
    [InlineData("new List<int> { 1, 2, 3 }.ConvertAll(x => x.ToString())[0]", "1")]
    [InlineData("new List<int> { 1, 2, 3, 4 }.RemoveAll(x => x % 2 == 0)", 2)]
    [InlineData("new List<int> { 1, 2, 3 }.FindAll(x => x > 1).Count", 2)]
    public void LambdaBindsToNonFuncDelegates(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("\"ab\".Sum(c => c)", 195)]
    [InlineData("new byte[] { 1, 2, 3 }.Sum(b => b)", 6)]
    [InlineData("new short[] { 1, 2 }.Sum(s => s)", 3)]
    [InlineData("new byte[] { 1, 2 }.Average(b => b)", 1.5)]
    [InlineData("new[] { 1, 2 }.Sum(x => x * 1.5)", 4.5)]
    public void LambdaReturnImplicitConversion(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);
}
