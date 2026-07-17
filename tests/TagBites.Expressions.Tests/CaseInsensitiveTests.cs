using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class IgnoreCaseTests : ExpressionTestBase
{
    [Theory]
    [InlineData("VALUE", 5)]
    [InlineData("value", 5)]
    [InlineData("Value + value + VALUE", 15)]
    public void Parameters_CanBeIgnoreCase(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            IgnoreCase = true,
            Parameters = { (typeof(int), "Value") }
        };

        ExecuteAndTest(script, options, expectedResult, 5);
    }

    [Fact]
    public void Parameters_AreCaseSensitiveByDefault()
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(int), "Value") }
        };

        Assert.False(ExpressionParser.TryParse("VALUE", options, out _, out var error));
        Assert.Contains("VALUE", error);
    }

    [Fact]
    public void GlobalMembers_CanBeCaseInsensitive()
    {
        var options = new ExpressionParserOptions
        {
            IgnoreCase = true,
            GlobalMembers = { { "Value", (null, 5) } }
        };

        ExecuteAndTest("value", options, 5);
        ExecuteAndTest("VALUE", options, 5);
    }

    [Theory]
    [InlineData("m.value", 5)]
    [InlineData("m.VALUE", 5)]
    [InlineData("m.getvalue(\"TimesTwo\")", 10)]
    public void InstanceMembersAndMethods_CanBeIgnoreCase(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            IgnoreCase = true,
            Parameters = { (typeof(TestModel), "m") }
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel(5));
    }

    [Fact]
    public void InstanceMembers_AreCaseSensitiveByDefault()
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(TestModel), "m") }
        };

        Assert.False(ExpressionParser.TryParse("m.VALUE", options, out _, out var error));
        Assert.Contains("VALUE", error);
    }

    [Fact]
    public void ExtensionMethods_CanBeCaseInsensitive()
    {
        var options = new ExpressionParserOptions
        {
            IgnoreCase = true,
            Parameters = { (typeof(TestModel), "m") },
            IncludedTypes = { typeof(TestModelExtensions) }
        };

        ExecuteAndTest("m.getvalueextension(2)", options, 2, new TestModel());
    }

    [Fact]
    public void IncludedTypes_CanBeCaseInsensitive()
    {
        var options = new ExpressionParserOptions
        {
            IgnoreCase = true,
            IncludedTypes = { typeof(TestModel) }
        };

        var result = Execute("testmodel.StaticVoidMethod(1)", options);

        Assert.Null(result);
    }

    [Fact]
    public void ObjectInitializerMembers_CanBeCaseInsensitive()
    {
        var options = new ExpressionParserOptions
        {
            IgnoreCase = true,
            IncludedTypes = { typeof(TestModel) }
        };

        ExecuteAndTest("new TestModel { PROPERTY1 = 7 }.Property1", options, 7);
    }

    [Fact]
    public void DeclaredPatternVariables_CanBeCaseInsensitive()
    {
        var options = new ExpressionParserOptions
        {
            IgnoreCase = true,
            Parameters = { (typeof(int), "n") }
        };

        ExecuteAndTest("n switch { int A => a + 1 }", options, 8, 7);
    }

    [Fact]
    public void AnonymousObjectMembers_CanBeCaseInsensitive()
    {
        var options = new ExpressionParserOptions { IgnoreCase = true };

        ExecuteAndTest("new { X = 1 }.x", options, 1);
    }
}
