namespace TagBites.Expressions.Tests;

public class AnonymousObjectTests : ExpressionTestBase
{
    [Theory]
    [InlineData("new { X = 1, Y = 2 }.X", 1)]
    [InlineData("new { X = 1, Y = 2 }.Y", 2)]
    [InlineData(@"new { Name = ""Bob"", Age = 30 }.Name", "Bob")]
    [InlineData("new { A = 1 }.A + new { B = 2 }.B", 3)]
    [InlineData("new[] { 1, 2, 3 }.Select(v => new { Doubled = v * 2 }).Sum(v => v.Doubled)", 12)]
    public void AnonymousObjectCreation(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Fact]
    public void AnonymousObjectCreation_InferredMemberName()
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int), "x") } };
        ExecuteAndTest("new { x }.x", options, 5, 5);
    }

    [Fact]
    public void AnonymousObjectCreation_ResultAccessibleViaDynamic()
    {
        var result = ExpressionParser.Invoke(@"new { State = ""CA"", CalculatedValue = 42 }");

        Assert.IsAssignableFrom<IDictionary<string, object>>(result);

        dynamic value = result!;
        Assert.Equal("CA", value.State);
        Assert.Equal(42, value.CalculatedValue);
    }

    [Fact]
    public void AnonymousObjectCreation_MissingMemberOnLiteral_FailsAtParseTime()
    {
        Assert.False(ExpressionParser.TryParse("new { X = 1 }.DoesNotExist", null, out _, out var error));
        Assert.Contains("DoesNotExist", error);
    }

    [Fact]
    public void AnonymousObjectCreation_MissingMemberViaIndirection_FailsAtParseTime()
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int[]), "arr") } };
        Assert.False(ExpressionParser.TryParse("arr.Select(x => new { Doubled = x * 2 }).First().DoesNotExist", options, out _, out var error));
        Assert.Contains("DoesNotExist", error);
    }

    [Fact]
    public void AnonymousObjectCreation_ManyDistinctShapes_HasNoPracticalLimit()
    {
        var members = string.Join(", ", Enumerable.Range(0, 300).Select(i => $"new {{ M{i} = {i} }}.M{i}"));

        var expression = ExpressionParser.Parse($"new object[] {{ {members} }}");
        var values = (object[])expression.Compile().DynamicInvoke()!;
        Assert.Equal(Enumerable.Range(0, 300).Cast<object>(), values);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public void AnonymousObjectCreation_SameShapeAcrossTernaryBranches_SharesOneSlotType(bool condition, int expected)
    {
        ExecuteAndTest("(condition ? new { A = 1 } : new { A = 2 }).A",
            new ExpressionParserOptions { Parameters = { (typeof(bool), "condition") } },
            expected, condition);
    }

    [Theory]
    [InlineData("new { A = 1, B = \"x\" }.Equals(new { A = 1, B = \"x\" })", true)]
    [InlineData("new { A = 1, B = \"x\" }.Equals(new { A = 2, B = \"x\" })", false)]
    [InlineData("new { A = 1 }.Equals(new { B = 1 })", false)]
    public void AnonymousObjectCreation_Equals_IsValueEquality(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Fact]
    public void AnonymousObjectCreation_GroupByDistinct_UseValueEquality()
    {
        var script = "new[] { 1, 1, 2, 2, 2, 3 }.Select(x => new { Group = x }).Distinct().Count()";
        var result = ExpressionParser.Invoke<int>(script);

        Assert.Equal(3, result);
    }
}
