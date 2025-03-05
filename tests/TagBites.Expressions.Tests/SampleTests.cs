namespace TagBites.Expressions.Tests;

public class SampleTests
{
    [Fact]
    public void BasicTest()
    {
        var result = ExpressionParser.Invoke("5 / 2.5");
        Assert.Equal(2d, result);

        var result2 = ExpressionParser.Invoke<int>("new [] { 1, 2, 3 }.Select(x => (x, x + 1).Item2).Sum()");
        Assert.Equal(9, result2);

        var result3 = ExpressionParser.Invoke<int>("(a + b) / 2", ("a", 2), ("b", 4));
        Assert.Equal(3, result3);
    }

    [Fact]
    public void GlobalMembersTest()
    {
        var expressionText = "a switch { 1 => b, 2 => b * 2, _ => b + a }";

        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(int), "a")
            },
            GlobalMembers =
            {
                {"b", (null, 2)}
            }
        };
        var func = ExpressionParser.Compile<Func<int, int>>(expressionText, options);

        Assert.Equal(2, func(1));
        Assert.Equal(4, func(2));
        Assert.Equal(5, func(3));
    }

    [Fact]
    public void TypeTest()
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(TestModel), "this")
            },
            UseFirstParameterAsThis = true
        };

        var m = new TestModel { X = 1, Y = 2 };

        Assert.Equal(3, ExpressionParser.Invoke("this.X + this.Y", options, m));
        Assert.Equal(3, ExpressionParser.Invoke("X + Y", options, m));
        Assert.Equal(3, ExpressionParser.Invoke("X + new TestModel { X = 1, Y = 2 }.Y", options, m));
    }

    private class TestModel
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}
