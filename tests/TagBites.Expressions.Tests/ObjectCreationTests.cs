using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class ObjectCreationTests : ExpressionTestBase
{
    [Theory]
    [InlineData("new DateTime(1992, 8, 7) < new DateTime(2021, 8, 14)", true)]
    [InlineData("new List<int>() != null", true)]
    public void NewOperator(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("new int[-1]")]
    [InlineData("new int[2, -1]")]
    [InlineData("new int[1 - 2]")]
    public void NegativeArraySize_Throws(string script) => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script));

    [Theory]
    [InlineData("new TestModel().Value", 1)]
    [InlineData("new TestModel(5).Value", 5)]
    [InlineData("new TestModel { Property1 = 1, Property2 = 2 }.Property1", 1)]
    [InlineData("new TestModel { Property1 = 1, Property2 = 2 }.Property2", 2)]
    [InlineData("new TestModel { Property1 = 0, Property2 = 0 }.Value", 1)]
    [InlineData("new TestModel(5) { Property1 = 1, Property2 = 2 }.Value", 5)]
    [InlineData("new TestModel { Property1 = (byte)3 }.Property1", 3)]
    [InlineData("new TestModel { Property1 = 'a' }.Property1", 97)]
    [InlineData("new TestModel { Ratio = 1 }.Ratio", 1.0)]
    [InlineData("new TestModel { Ratio = 2L }.Ratio", 2.0)]
    [InlineData("new TestModel { Ratio = 1.5f }.Ratio", 1.5)]
    [InlineData("new TestModel { Total = 3 }.Total == 3m", true)]
    [InlineData("new TestModel { Optional = 4 }.Optional", 4)]
    [InlineData("new TestModel { Optional = null }.Optional", null)]
    [InlineData("new TestModel { Ratio = 1, Total = 2, Optional = 3 }.Ratio", 1.0)]
    public void ObjectCreation(string script, object? expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            IncludedTypes =
            {
                typeof(TestModel)
            }
        };
        ExecuteAndTest(script, options, expectedResult);
    }

    [Theory]
    [InlineData("new ()")]
    [InlineData("new () { Property1 = 1 }")]
    public void TargetObjectCreation(string script)
    {
        var options = new ExpressionParserOptions
        {
            ResultType = typeof(TestModel)
        };
        var result = Execute(script, options);

        Assert.IsType<TestModel>(result);
    }

    [Theory]
    [InlineData("new TestModel(1)", typeof(TestModel))]
    public void ResolveTypeUsingResult(string script, Type type)
    {
        var options = new ExpressionParserOptions
        {
            AllowRuntimeCast = true,
            ResultType = type
        };
        var result = Execute(script, options);
        Assert.IsType(type, result);
    }

    [Theory]
    [InlineData("m.TakeProperty1(new())", 0)]
    [InlineData("m.TakeProperty1(new() { Property1 = 7 })", 7)]
    [InlineData("m.Pick(new())", "model")]
    [InlineData("(true ? new() { Property1 = 3 } : m).Property1", 3)]
    [InlineData("((TestModel)new()).Value", 1)]
    [InlineData("(m ?? new() { Property1 = 9 }).Property1", 4)]
    [InlineData("new List<TestModel> { new() { Property1 = 9 } }[0].Property1", 9)]
    public void TargetTypedNewAsArgument(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            IncludedTypes = { typeof(TestModel) },
            Parameters = { (typeof(TestModel), "m") }
        };
        ExecuteAndTest(script, options, expectedResult, new TestModel { Property1 = 4 });
    }

    [Theory]
    [InlineData("new()")]
    [InlineData("1 + new()")]
    public void TargetTypedNew_WithoutTarget_Throws(string script) => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script, null));
}
