namespace TagBites.Expressions.Tests;

public class DetectIdentifiersTests : ExpressionTestBase
{
    [Fact]
    public void SeparatesKnownFromUnknown()
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int), "a"), (typeof(int), "b") } };

        var (identifiers, unknown) = ExpressionParser.DetectIdentifiers("a + b + c", options);

        Assert.Contains("a", identifiers);
        Assert.Contains("b", identifiers);
        Assert.DoesNotContain("c", identifiers);
        Assert.Contains("c", unknown);
    }
}
