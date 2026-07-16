using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

/// <summary>
/// Non-standard custom operators enabled by <see cref="ExpressionParserOptions.AllowRuntimeCast"/>:
/// <c>typeis</c> / <c>typeas</c> / <c>typecast</c> against runtime type names. Not valid C#.
/// </summary>
public class RuntimeCastTests : ExpressionTestBase
{
    [Theory]
    [InlineData("""typeis("a", "System.String,System.Private.CoreLib")""", true)]
    [InlineData("""typeis(m, "System.String,System.Private.CoreLib")""", false)]
    [InlineData("""typeis(null, "System.String,System.Private.CoreLib")""", false)]
    [InlineData("""typeas("a", "System.String,System.Private.CoreLib")""", "a")]
    [InlineData("""typeas(null, "System.String,System.Private.CoreLib")""", null)]
    [InlineData("""typecast(null, "System.String,System.Private.CoreLib")""", null)]
    [InlineData("""typecast("a", "System.String,System.Private.CoreLib")""", "a")]
    public void RuntimeCast(string script, object? expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            AllowRuntimeCast = true,
            IncludedTypes =
            {
                typeof(TestModel),
                typeof(ITestModel),
            },
            Parameters = { (typeof(TestModel), "m") }
        };
        ExecuteAndTest(script, options, expectedResult, new TestModel());
    }
}
