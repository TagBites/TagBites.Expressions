using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class BindingTests : ExpressionTestBase
{
    [Theory]
    [InlineData("ChildTimesTen.Value + Value + v", 16)]
    public void ThisParameter(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(TestModel), "m"),
                (typeof(int), "v")
            },
            UseFirstParameterAsThis = true
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel(), 5);
    }

    [Theory]
    [InlineData("GetValue(\"TimesTwo\")", 2)]
    [InlineData("v == 5", true)]
    [InlineData("nv.HasValue && nv == 5", true)]
    [InlineData("Add.Invoke(2, 3)", 5)]
    [InlineData("Add?.Invoke(2, 3)", 5)]
    [InlineData("Add(2, 3)", 5)]
    [InlineData("m.ChildTimesTen.Value", 10)]
    [InlineData("m?.ChildTimesTen.Value", 10)]
    [InlineData("m.GetValue(\"TimesTwo\")", 2)]
    [InlineData("this.GetValue(\"TimesTwo\")", 2)]
    [InlineData("this.ChildTimesTen.Value", 10)]
    [InlineData("ChildTimesTen.Value", 10)]
    [InlineData("ReturnArgument<int>(2)", 2)]
    [InlineData("this.ReturnArgument<int>(2)", 2)]
    [InlineData("m.ReturnArgument<int>(2)", 2)]
    public void GlobalMembers(string script, object? expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            GlobalMembers =
            {
                {"this", (null, new TestModel())},
                {"m", (null, new TestModel())},
                {"v", (null, 5)},
                {"nv", (typeof(int?), 5)},
                {"Add", (null, (Func<int, int, int>)Add)},
            }
        };
        ExecuteAndTest(script, options, expectedResult);

        int Add(int a, int b) => a + b;
    }

    [Fact]
    public void FuncAsParameter()
    {
        var options = new ExpressionParserOptions
        {
            Parameters = {
                (typeof(Func<object?, object?>), "a"),
            }
        };

        Assert.Null(Execute("a?.Invoke(42)", options, [null]));
        Assert.Equal(Execute("a?.Invoke(42)", options, (Func<object, object>)(x => x)), 42);
        Assert.Equal(Execute("a(42)", options, (Func<object, object>)(x => x)), 42);
    }

    [Fact]
    public void ActionAsParameter()
    {
        var options = new ExpressionParserOptions
        {
            Parameters = {
                (typeof(Action<object>), "a")
            },
            IncludedTypes =
            {
                typeof(TestModel)
            }
        };
        var voidCallCount = 0;

        Assert.Null(Execute("a(42)", options, (Action<object>)VoidMethod));
        Assert.Null(Execute("a.Invoke(42)", options, (Action<object>)VoidMethod));
        Assert.Null(Execute("a?.Invoke(42)", options, (Action<object>)VoidMethod));
        Assert.Null(Execute("a?.Invoke(42)", options, [null]));
        Assert.Equal(3, voidCallCount);

        Assert.Null(Execute("TestModel.StaticVoidMethod(42)", options, (Action<object>)VoidMethod));

        void VoidMethod(object x) => ++voidCallCount;
    }
}
