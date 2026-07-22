namespace TagBites.Expressions.Tests;

public class TupleTests : ExpressionTestBase
{
    private const string SeqLoHi = "Enumerable.Range(1, 2).Select(i => (Lo: i, Hi: i * 10))";
    private const string SeqXy = "Enumerable.Range(1, 2).Select(i => (X: i, Y: i * 100))";
    private const string SeqLoY = "Enumerable.Range(1, 2).Select(i => (Lo: i, Y: i * 5))";
    private const string SeqUnnamed = "Enumerable.Range(1, 2).Select(i => (i, i * 100))";

    [Theory]
    [InlineData("(1, 2).Item1", 1)]
    [InlineData("(1, 2).Item2", 2)]
    [InlineData("(n, n).Item1", 1)]
    [InlineData("(n: n, n).n", 1)]
    [InlineData("(n, n + 1, a + a).Item1", 1)]
    [InlineData("(n, n + 1, a + a).n", 1)]
    [InlineData("(n, n + 1, a + a).Item2", 2)]
    [InlineData("(n, n + 1, a + a).Item3", "aa")]
    [InlineData("(A: 1, B: 2).Item1", 1)]
    [InlineData("(A: 1, B: 2).Item2", 2)]
    [InlineData("(A: 1, B: 2).A", 1)]
    [InlineData("(A: 1, B: 2).B", 2)]
    [InlineData("(A: n, B: n + 1, C: a + a).A", 1)]
    [InlineData("(A: n, B: n + 1, C: a + a).C", "aa")]
    [InlineData("(1, B: 2).B", 2)]
    [InlineData("(A: 1, B: 2).A + (A: 1, B: 2).B", 3)]
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
    [InlineData("(n, n + 1).n", 1)]
    [InlineData("(d.Day, 5).Day", 15)]
    [InlineData("(d.Day, d.Month).Month", 7)]
    [InlineData("((1, 2, 3).Item2, 5, 6).Item2", 5)]
    public void TupleImplicitNames(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(int), "n"), (typeof(DateTime), "d") }
        };

        ExecuteAndTest(script, options, expectedResult, 1, new DateTime(2020, 7, 15));
    }

    [Theory]
    [InlineData("(d.Day, d.Day).Day")]
    [InlineData("(n, n).n")]
    public void TupleImplicitNames_DroppedOnDuplicate(string script)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(int), "n"), (typeof(DateTime), "d") }
        };

        Assert.False(ExpressionParser.TryParse(script, options, out _, out var error));
        Assert.Contains("Unknown member", error);
    }

    [Theory]
    [InlineData("(Item1: 1, 2)", true)]
    [InlineData("(1, Item2: 2)", true)]
    [InlineData("(Item2: 1, 2)", false)]
    [InlineData("(1, Item1: 2)", false)]
    [InlineData("(Rest: 1, 2)", false)]
    [InlineData("(ToString: 1, 2)", false)]
    public void TupleExplicitReservedNames(string script, bool valid)
    {
        Assert.Equal(valid, ExpressionParser.TryParse(script, null, out _, out _));
    }

    [Theory]
    [InlineData("(1, 2) == (1, 2)", true)]
    [InlineData("(1, 2) == (1, 3)", false)]
    [InlineData("(1, 2) != (1, 3)", true)]
    [InlineData("""("a", 1) == ("a", 1)""", true)]
    [InlineData("(1, 2, 3) == (1, 2, 3)", true)]
    public void TupleEquality(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("people.Select(x => (Age: x, AgeX2: x * 2)).First().Age", 7)]
    [InlineData("people.Select(x => (Age: x, AgeX2: x * 2)).First().AgeX2", 14)]
    [InlineData("people.Select(x => (Age: x, AgeX2: x * 2)).First().Item1", 7)]
    [InlineData("people.Select(x => (Age: x, AgeX2: x * 2)).Last().Age", 9)]
    [InlineData("people.Select(x => (Age: x, AgeX2: x * 2)).Where(t => t.Age > 7).First().AgeX2", 16)]
    [InlineData("people.Select(x => (Age: x, AgeX2: x * 2)).Select(t => t.Age + t.AgeX2).First()", 21)]
    [InlineData("people.Select(x => (Age: x, AgeX2: x * 2)).OrderBy(t => t.Age).First().Age", 7)]
    [InlineData("people.Select(x => (Age: x, AgeX2: x * 2)).OrderByDescending(t => t.Age).First().Age", 9)]
    [InlineData("people.Select(x => (Age: x, AgeX2: x * 2)).Skip(1).First().Age", 8)]
    [InlineData("people.Select(x => (Age: x, AgeX2: x * 2)).ToList()[0].Age", 7)]
    [InlineData("people.Select(x => (Inner: (A: x, B: x + 1), C: x * 3)).First().Inner.B", 8)]
    [InlineData("people.Select(x => (Obj: new { V = x }, N: x)).First().Obj.V", 7)]
    [InlineData("people.Select(x => new { Pair = (Lo: x, Hi: x * 2) }).First().Pair.Hi", 14)]
    [InlineData("people.Select(x => new { Pair = (Lo: x, Hi: x * 2) }).First().Pair.Item2", 14)]
    public void NamedTupleFlow(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(IEnumerable<int>), "people") }
        };

        ExecuteAndTest(script, options, expectedResult, new List<int> { 7, 8, 9 });
    }

    [Theory]
    [InlineData("(A: new { X = 1 }, B: 2).A.X", 1)]
    [InlineData("(A: new { X = 1 }, B: 2).B", 2)]
    [InlineData("new { T = (A: 1, B: 2) }.T.A", 1)]
    [InlineData("new { T = (A: 1, B: 2) }.T.B", 2)]
    [InlineData("new { T = (A: 1, B: 2) }.T.Item1", 1)]
    [InlineData("new { Outer = new { Inner = (A: 1, B: 2) } }.Outer.Inner.B", 2)]
    [InlineData("new { T = (A: 1, B: 2) }.T.A + new { T = (X: 10, Y: 20) }.T.X", 11)]
    public void NamedTupleWithAnonymousObject(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 4)]
    public void NamedTupleInAnonymous_SameShapeAcrossTernaryBranches(bool condition, int expected)
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(bool), "cond") } };
        ExecuteAndTest("(cond ? new { T = (A: 1, B: 2) } : new { T = (A: 3, B: 4) }).T.B", options, expected, condition);
    }

    [Theory]
    [InlineData(SeqLoHi + ".Concat(" + SeqXy + ").First().Item1", 1)]
    [InlineData(SeqLoHi + ".Concat(" + SeqLoHi + ").First().Lo", 1)]
    [InlineData(SeqLoHi + ".Concat(" + SeqLoHi + ").First().Hi", 10)]
    [InlineData(SeqLoHi + ".Concat(" + SeqLoY + ").First().Lo", 1)]
    public void NamedTupleNameMerge_Kept(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData(SeqLoHi + ".Concat(" + SeqXy + ").First().Lo")]
    [InlineData(SeqLoHi + ".Concat(" + SeqXy + ").First().X")]
    [InlineData(SeqLoHi + ".Concat(" + SeqUnnamed + ").First().Lo")]
    [InlineData(SeqLoHi + ".Concat(" + SeqLoY + ").First().Hi")]
    public void NamedTupleNameMerge_DroppedOnConflict(string script)
    {
        Assert.False(ExpressionParser.TryParse(script, null, out _, out var error));
        Assert.Contains("Unknown member", error);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void NamedTupleNameMerge_RespectsIgnoreCase(bool ignoreCase, bool resolves)
    {
        const string script = SeqLoHi + ".Concat(Enumerable.Range(1, 2).Select(i => (lo: i, hi: i * 100))).First().LO";
        var options = new ExpressionParserOptions { IgnoreCase = ignoreCase };

        if (resolves)
            ExecuteAndTest(script, options, 1);
        else
            Assert.False(ExpressionParser.TryParse(script, options, out _, out _));
    }
}
