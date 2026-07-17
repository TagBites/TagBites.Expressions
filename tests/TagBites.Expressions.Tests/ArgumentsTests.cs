namespace TagBites.Expressions.Tests;

public class ArgumentsTests : ExpressionTestBase
{
    [Fact]
    public void InfersTypeFromValue()
    {
        ExpressionArgument a = ("x", 5);

        Assert.Equal("x", a.Name);
        Assert.Equal(5, a.Value);
        Assert.Equal(typeof(int), a.Type);
    }

    [Fact]
    public void NullValueWithoutTypeIsObject()
    {
        ExpressionArgument a = ("x", null);

        Assert.Null(a.Value);
        Assert.Equal(typeof(object), a.Type);
    }

    [Fact]
    public void ExplicitTypeIsHonored()
    {
        ExpressionArgument a = ("x", null, typeof(string));

        Assert.Equal(typeof(string), a.Type);
    }
}
