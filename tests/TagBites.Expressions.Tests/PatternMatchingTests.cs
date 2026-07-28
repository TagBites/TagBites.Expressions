using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class PatternMatchingTests : ExpressionTestBase
{
    [Theory]
    [InlineData("1 switch { 1 => 10, 2 => 20, _ => 0 }", 10)]
    [InlineData("2 switch { 1 => 10, 2 => 20, _ => 0 }", 20)]
    [InlineData("3 switch { 1 => 10, 2 => 20, _ => 0 }", 0)]
    [InlineData("n switch { 1 => 10, 2 => 20, _ => 0 }", 10)]
    [InlineData("5 switch { > 3 => 1, _ => 0 }", 1)]
    [InlineData("5 switch { > 3 and < 10 => 1, _ => 0 }", 1)]
    [InlineData("5 switch { 1 or 5 => 1, _ => 0 }", 1)]
    [InlineData("5 switch { not 5 => 0, _ => 1 }", 1)]
    [InlineData("5 switch { 5 when 1 > 2 => 1, 5 => 2, _ => 0 }", 2)]
    [InlineData("n switch { int a => a + 1 }", 2)]
    [InlineData("1 switch { 1 => 1, _ => 2L }", 1L)]
    [InlineData("n switch { int a when a > 5 => a, int a => a * 10 }", 10)]
    [InlineData("n switch { 1 => 1, int a when a > 0 => a, int a => -a }", 1)]
    public void Switch(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(int), "n"),
            }
        };

        ExecuteAndTest(script, options, expectedResult, 1);
    }

    [Theory]
    [InlineData("nv switch { null => -1, _ => 1 }", -1, null)]
    [InlineData("nv switch { null => -1, int a => a * 2 }", 14, 7)]
    [InlineData("nv switch { > 5 => 1, _ => 0 }", 1, 7)]
    public void SwitchOnNullable(string script, object expectedResult, int? value)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(int?), "nv") }
        };

        ExecuteAndTest(script, options, expectedResult, value);
    }

    [Theory]
    [InlineData("m is TestModel", true)]
    [InlineData("m is object", true)]
    [InlineData("!(m is TestModel)", false)]
    [InlineData("m as TestModel != null", true)]
    [InlineData("(int?)1 is int", true)]
    [InlineData("(int?)null is int", false)]
    [InlineData("(int?)1 as int?", 1)]
    [InlineData("(int?)null as int?", null)]
    [InlineData("((ITestModel)m) as TestModel != null", true)]
    [InlineData("new[] { 1, 2, 3 }.ToList() is List<int>", true)]
    [InlineData("((object)\"x\") is List<int>", false)]
    [InlineData("new[] { 1 } is int[]", true)]
    [InlineData("((object)new List<int> { 1 }) as List<int> != null", true)]
    [InlineData("((object)5) as List<int> == null", true)]
    public void IsAndAsOperators(string script, object? expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            IncludedTypes =
            {
                typeof(ITestModel)
            },
            Parameters =
            {
                (typeof(TestModel), "m"),
                (typeof(int?), "nv"),
            }
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel(), 5);
    }

    [Theory]
    [InlineData("(int?)null is null", true)]
    [InlineData("(int?)1 is null", false)]
    [InlineData("(int?)1 is not null", true)]
    [InlineData("(int?)null is not null", false)]
    [InlineData("(int?)1 is { }", true)]
    [InlineData("(int?)null is { }", false)]
    [InlineData("(int?)1 is not { }", false)]
    [InlineData("(int?)null is not  { }", true)]
    public void PatternNullCheck(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("(int?)1 is int", true)]
    [InlineData("(int?)null is int", false)]
    [InlineData("true is int", false)]
    public void PatternTypeCheck(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 is 1", true)]
    [InlineData("1 is 0", false)]
    [InlineData("1 is (0 or 1)", true)]
    [InlineData("1 is not 0", true)]
    [InlineData("2 is not (0 or 1)", true)]
    [InlineData("(int?)1 is 1", true)]
    [InlineData("(int?)1 is 1 or 2", true)]
    [InlineData("(int?)1 is 0", false)]
    [InlineData("DayOfWeek.Wednesday is DayOfWeek.Wednesday", true)]
    [InlineData("DayOfWeek.Monday is DayOfWeek.Wednesday", false)]
    [InlineData("(DayOfWeek)3 is DayOfWeek.Wednesday", true)]
    [InlineData("(DayOfWeek)1 is DayOfWeek.Wednesday or DayOfWeek.Monday", true)]
    [InlineData("(DayOfWeek)1 is (DayOfWeek.Wednesday or DayOfWeek.Monday)", true)]
    [InlineData("(DayOfWeek)1 is not DayOfWeek.Wednesday", true)]
    [InlineData("(DayOfWeek)2 is not (DayOfWeek.Wednesday or DayOfWeek.Monday)", true)]
    public void PatternConstCheck(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 is 1", true)]
    [InlineData("1 is 0 or 1", true)]
    [InlineData("1 is 1 and > 0", true)]
    [InlineData("((object)7) is int and > 5", true)]
    [InlineData("((object)3) is int and > 5", false)]
    [InlineData("((object)7) is int and (> 5 and < 10)", true)]
    [InlineData("((object)\"ab\") is string and { Length: 2 }", true)]
    [InlineData("((object)7) switch { int and > 5 => \"big\", int => \"int\", _ => \"other\" }", "big")]
    [InlineData("((object)2) switch { int and > 5 => \"big\", int => \"int\", _ => \"other\" }", "int")]
    public void PatternOrAnd(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 is > 0", true)]
    [InlineData("1 is >= 0", true)]
    [InlineData("1 is < 0", false)]
    [InlineData("1 is <= 0", false)]
    [InlineData("DayOfWeek.Wednesday is >= DayOfWeek.Monday", true)]
    [InlineData("DayOfWeek.Sunday is < DayOfWeek.Monday", true)]
    [InlineData("DayOfWeek.Wednesday is >= DayOfWeek.Monday and <= DayOfWeek.Friday", true)]
    [InlineData("DayOfWeek.Saturday switch { >= DayOfWeek.Monday and <= DayOfWeek.Friday => \"weekday\", _ => \"weekend\" }", "weekend")]
    public void PatternRelation(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 is not 0", true)]
    public void PatternUnaryOperator(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 is (1)", true)]
    [InlineData("1 is (2 or 1)", true)]
    [InlineData("1 is (1 or 2)", true)]
    [InlineData("1 is not 0 and (1 or 2)", true)]
    [InlineData("1 is not (1 or 2)", false)]
    [InlineData("1 is not (1 or 2 or not 3)", false)]
    [InlineData("(int?)1 is not (null)", true)]
    [InlineData("(int?)1 is (null)", false)]
    [InlineData("(int?)1 is (null or 1)", true)]
    public void PatternParenthesized(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("(int?)1 is { } a", true)]
    [InlineData("(int?)1 is int a && a == 1", true)]
    [InlineData("(int?)1 is not int a && a == 1", false)]
    [InlineData("(int?)1 is not int a || a == 1", true)]
    [InlineData("(int?)1 is int a && list.Sum(x => x + a) == 9", true)]
    [InlineData("(int?)1 is int a && list.Sum(x => x is int x2 ? x2 + a : 0) == 9", true)]
    public void PatternVar(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            IncludedTypes =
            {
                typeof(ITestModel)
            },
            Parameters =
            {
                (typeof(IList<int>), "list"),
                (typeof(TestModel), "m"),
                (typeof(int?), "nv"),
            }
        };

        ExecuteAndTest(script, options, expectedResult, new List<int> { 1, 2, 3 }, new TestModel(), 5);
    }

    [Theory]
    [InlineData("\"a\" is { Length: > 0 }", true)]
    [InlineData("\"a\" is { Length: < 0 }", false)]
    [InlineData("s is { X: 1 }", true)]
    [InlineData("s is { X: 1, Y: 2 }", true)]
    [InlineData("s is { X: 1, Y: 3 }", false)]
    [InlineData("s is { X: 3, Y: 2 }", false)]
    [InlineData("s is TestStruct { X: 1, Y: 2 } a && a.X + a.Y == 3", true)]
    [InlineData("m is { ChildNull: null }", true)]
    [InlineData("m is { ChildNull: not null }", false)]
    [InlineData("m is { ChildNull: not { } }", true)]
    [InlineData("m is { ChildNull: { } }", false)]
    [InlineData("m is { ChildNull: null, ChildTimesTen: { } }", true)]
    [InlineData("m is { ChildNull: null, ChildTimesTen: not { } }", false)]
    [InlineData("m is TestModel { ChildNull: null, ChildTimesTen: { } } a && a.ChildTimesTen.Value == 10", true)]
    [InlineData("m is { ChildTimesTen.Value: 10 }", true)]
    [InlineData("m is { ChildTimesTen.Value: 11 }", false)]
    [InlineData("m is { ChildNull.Value: 1 }", false)]
    [InlineData("s is { X: 1, Y: 2 } && \"ab\" is { Length: 2 }", true)]
    public void PatternProperty(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(TestStruct), "s"),
                (typeof(TestModel), "m")
            }
        };
        ExecuteAndTest(script, options, expectedResult, new TestStruct { X = 1, Y = 2 }, new TestModel());
    }

    [Theory]
    [InlineData("(1, 2) is (int a, int b) && a < b", true)]
    [InlineData("(1, 2) is (1, 2)", true)]
    [InlineData("(1, 2) is (1, var b) && b == 2", true)]
    [InlineData("(1, 2) is (2, _)", false)]
    [InlineData("(1, 2) is (1, _)", true)]
    [InlineData("(1, 2, 3) is (1, 2, 3)", true)]
    [InlineData("(1, 2, 3) is (1, _, 3)", true)]
    [InlineData("t is (int a, string b) && a == 5 && b == \"x\"", true)]
    [InlineData("(1, 2) is var (a, b) && a < b", true)]
    [InlineData("(1, 2) is var (a, b) && a > b", false)]
    [InlineData("(10, (20, 30)) is var (a, (b, c)) && a + b + c == 60", true)]
    [InlineData("(1, 2) is var (a, _) && a == 1", true)]
    public void PatternTuple(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof((int, string)), "t") } };
        ExecuteAndTest(script, options, expectedResult, (5, "x"));
    }

    [Theory]
    [InlineData("arr is [1, 2, 3]", true)]
    [InlineData("arr is [1, 2]", false)]
    [InlineData("arr is [1, 2, 4]", false)]
    [InlineData("arr is [1, .., 3]", true)]
    [InlineData("arr is [1, 2, .., 3]", true)]
    [InlineData("arr is [.., 3]", true)]
    [InlineData("arr is [1, ..]", true)]
    [InlineData("arr is [..]", true)]
    [InlineData("arr is [var a, var b, var c] && a + b + c == 6", true)]
    [InlineData("arr is [1, 2, 3] r && r.Length == 3", true)]
    [InlineData("arr is [> 0, > 0, > 0]", true)]
    public void PatternList(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int[]), "arr") } };
        ExecuteAndTest(script, options, expectedResult, new[] { 1, 2, 3 });
    }

    [Fact]
    public void PatternList_NullArray_DoesNotMatch()
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int[]), "arr") } };
        ExecuteAndTest("arr is [1, 2, 3]", options, false, [null]);
    }

    [Fact]
    public void PatternList_OnIListImplementation()
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(IList<int>), "list") } };
        ExecuteAndTest("list is [1, 2, 3]", options, true, new List<int> { 1, 2, 3 });
    }
}
