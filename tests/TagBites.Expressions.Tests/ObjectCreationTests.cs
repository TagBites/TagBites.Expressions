using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class ObjectCreationTests : ExpressionTestBase
{
    [Theory]
    [InlineData("new DateTime(1992, 8, 7) < new DateTime(2021, 8, 14)", true)]
    [InlineData("new List<int>() != null", true)]
    public void NewOperator(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("new TestModel().Value", 1)]
    [InlineData("new TestModel(5).Value", 5)]
    [InlineData("new TestModel { Property1 = 1, Property2 = 2 }.Property1", 1)]
    [InlineData("new TestModel { Property1 = 1, Property2 = 2 }.Property2", 2)]
    [InlineData("new TestModel { Property1 = 0, Property2 = 0 }.Value", 1)]
    [InlineData("new TestModel(5) { Property1 = 1, Property2 = 2 }.Value", 5)]
    public void ObjectCreation(string script, object expectedResult)
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
}
