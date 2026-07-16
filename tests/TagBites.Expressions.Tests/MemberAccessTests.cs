using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class MemberAccessTests : ExpressionTestBase
{
    [Theory]
    [InlineData("new DateTime(2021, 8, 14).Day", 14)]
    [InlineData("new DateTime(2021, 8, 14).Date.Day", 14)]
    [InlineData("DateTime.MinValue < new DateTime(2021, 8, 14)", true)]
    public void MemberAccess(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("((TimeSpan?)TimeSpan.FromMinutes(2))?.TotalMinutes", 2d)]
    [InlineData("((TimeSpan?)TimeSpan.FromMinutes(2)).Value.TotalMinutes", 2d)]
    [InlineData("nv.Value", 5)]
    [InlineData("m?.ChildTimesTen != null", true)]
    [InlineData("m?.ChildTimesTen?.Value", 10)]
    [InlineData("nv + m.ChildTimesTen?.ChildTimesTen.Value + m.ChildTimesTen?.Value", 115)]
    [InlineData("(1 < 2 ? (int?)1 : 2).Value", 1)]
    [InlineData("(m?.ChildTimesTen.Value ?? nv).Value", 10)]
    public void NullConditionalAccess(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(TestModel), "m"),
                (typeof(int?), "nv"),
            }
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel(), 5);
    }
}
