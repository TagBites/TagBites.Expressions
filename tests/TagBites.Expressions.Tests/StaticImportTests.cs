using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class StaticImportTests : ExpressionTestBase
{
    [Theory]
    [InlineData("Sqrt(16)", 4d)]
    [InlineData("Abs(-5.0)", 5d)]
    [InlineData("Max(2, 7)", 7)]
    [InlineData("Min(Max(1, 4), 10)", 4)]
    [InlineData("Round(3.14159, 2)", 3.14d)]
    public void StaticMethod(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions { StaticImports = { typeof(Math) } };
        ExecuteAndTest(script, options, expectedResult);
    }

    [Fact]
    public void StaticConstant()
    {
        var options = new ExpressionParserOptions { StaticImports = { typeof(Math) } };
        ExecuteAndTest("PI", options, Math.PI);
        ExecuteAndTest("E", options, Math.E);
    }

    [Fact]
    public void QualifiedFormStillWorks()
    {
        var options = new ExpressionParserOptions { StaticImports = { typeof(Math) } };
        ExecuteAndTest("Math.Sqrt(16)", options, 4d);
    }

    [Fact]
    public void InstanceMemberTakesPrecedenceOverStaticImport()
    {
        var options = new ExpressionParserOptions
        {
            UseFirstParameterAsThis = true,
            Parameters = { (typeof(TestModel), "this") },
            StaticImports = { typeof(StaticTestClass) }
        };

        ExecuteAndTest("Value", options, new TestModel().Value, new TestModel());
        ExecuteAndTest("GetValue(\"TimesTwo\")", options, new TestModel().GetValue("TimesTwo"), new TestModel());
    }

    [Fact]
    public void UnknownStaticMemberStillFails()
    {
        var options = new ExpressionParserOptions { StaticImports = { typeof(Math) } };

        Assert.False(ExpressionParser.TryParse("Nonexistent(1)", options, out _, out _));
        Assert.False(ExpressionParser.TryParse("Nonexistent", options, out _, out _));
    }

    [Fact]
    public void NotImportedByDefault()
    {
        Assert.False(ExpressionParser.TryParse("Sqrt(16)", null, out _, out _));
    }

    [Fact]
    public void NonStaticClassThrows()
    {
        Assert.Throws<ArgumentException>(() => new ExpressionParserOptions { StaticImports = { typeof(TestModel) } });
    }
}
