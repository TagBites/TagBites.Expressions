namespace TagBites.Expressions.Tests;

public class SampleTests
{
    [Fact]
    public void BasicUseTest()
    {
        var expression = "new [] { 1, 2, 3 }.Select(x => (x, x + 1).Item2).Sum()";
        var func = ExpressionParser.Parse(expression, null).Compile();

        Assert.Equal(9, func.DynamicInvoke());
    }

    [Fact]
    public void SimpleTest()
    {
        var func = Parse("(a + b) / (double)b");
        Assert.Equal(2.5d, func(3, 2));

        func = Parse("a switch { 1 => b, 2 => b * 2, _ => b + a }");
        Assert.Equal(2, func(1, 2));
        Assert.Equal(4, func(2, 2));
        Assert.Equal(5, func(3, 2));

        static Func<int, int, double> Parse(string expression)
        {
            var options = new ExpressionParserOptions
            {
                Parameters =
                {
                    (typeof(int), "a"),
                    (typeof(int), "b")
                },
                ResultCastType = typeof(double)
            };
            var lambda = ExpressionParser.Parse(expression, options);
            return (Func<int, int, double>)lambda.Compile();
        }
    }

    [Fact]
    public void TypeTest()
    {
        var m = new TestModel { X = 1, Y = 2 };

        var func = Parse("X + Y");
        Assert.Equal(3, func(m));

        func = Parse("X + Nested.X");
        Assert.Equal(3, func(m));

        func = Parse("X + new TestModel { X = 1, Y = 2 }.Y");
        Assert.Equal(3, func(m));

        func = Parse("X + (X == 1 ? Nested.X : Nested.Y)");
        Assert.Equal(3, func(m));
        Assert.Equal(7, func(new TestModel { X = 2, Y = 3 }));

        static Func<TestModel, int> Parse(string expression)
        {
            var options = new ExpressionParserOptions
            {
                Parameters =
                {
                    (typeof(TestModel), "this")
                },
                UseFirstParameterAsThis = true,
                ResultType = typeof(int)
            };
            var lambda = ExpressionParser.Parse(expression, options);
            return (Func<TestModel, int>)lambda.Compile();
        }
    }

    private class TestModel
    {
        private TestModel? _nested;

        public int X { get; set; }
        public int Y { get; set; }
        public TestModel Nested => _nested ??= new TestModel { X = Y, Y = X + Y };
    }
}
