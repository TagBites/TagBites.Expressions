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

    [Theory]
    [InlineData("System.Math.PI")]
    [InlineData("System.DateTime.Now")]
    [InlineData("System.TimeSpan.FromMinutes(2)")]
    public void NamespaceQualifiedType_NotReportedAsUnknown(string script)
    {
        var (_, unknown) = ExpressionParser.DetectIdentifiers(script);

        Assert.DoesNotContain("System", unknown);
        Assert.Empty(unknown);
    }

    [Theory]
    [InlineData("System.TimeSpan.FromMinutes(a)", "a")]
    public void NamespaceQualifiedCall_DetectsArgumentIdentifier(string script, string expected)
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(double), "a") } };

        var (identifiers, unknown) = ExpressionParser.DetectIdentifiers(script, options);

        Assert.Contains(expected, identifiers);
        Assert.Empty(unknown);
    }

    [Fact]
    public void MultipleUnknowns_AreAllCollected()
    {
        var (_, unknown) = ExpressionParser.DetectIdentifiers("c + d");

        Assert.Contains("c", unknown);
        Assert.Contains("d", unknown);
    }
}
