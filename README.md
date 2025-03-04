# TagBites.Expressions

[![Nuget](https://img.shields.io/nuget/v/TagBites.Expressions.svg)](https://www.nuget.org/packages/TagBites.Expressions/)
[![License](https://img.shields.io/github/license/TagBites/TagBites.Expressions)](https://github.com/TagBites/TagBites.Expressions/blob/master/LICENSE)

Converts C# text expressions into LINQ expressions using **Roslyn**, supporting complete language syntax.

## Example

```csharp
public void BasicUseTest()
{
    var expression = "new [] { 1, 2, 3 }.Select(x => (x, x + 1).Item2).Sum()";
    var func = ExpressionParser.Parse(expression, null).Compile();

    Assert.Equal(9, func.DynamicInvoke());
}

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
```

## Benchmark

Benchmark for parsing the string `"Math.Pow(x, y) + 5"` into a LINQ expression.  
Comparison of TagBites (v. 1.0.5) and DynamicExpresso (v. 2.17.2).

| Method                            | Mean      | Error     | StdDev    | Allocated |
|---------------------------------- |----------:|----------:|----------:|----------:|
| TagBites                          |  8.355 us | 0.1583 us | 0.1555 us |    8.3 KB |
| TagBites_SharedOptions            |  7.780 us | 0.1424 us | 0.1262 us |   8.04 KB |
| DynamicExpresso                   | 26.860 us | 0.3166 us | 0.2962 us |  35.12 KB |
| DynamicExpresso_SharedInterpreter | 17.087 us | 0.0903 us | 0.0800 us |  16.64 KB |


```
[MemoryDiagnoser]
public class ParseToExpression
{
    private const string Script = "Math.Pow(x, y) + 5";

    private readonly ExpressionParserOptions _options = new()
    {
        Parameters =
        {
            (typeof(double), "x"),
            (typeof(double), "y")
        }
    };
    private readonly Interpreter _interpreter = new();


    [Benchmark]
    public void TagBites()
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(double), "x"),
                (typeof(double), "y")
            }
        };

        ExpressionParser.Parse(Script, options);
    }

    [Benchmark]
    public void TagBitesSharedOptions()
    {
        ExpressionParser.Parse(Script, _options);
    }

    [Benchmark]
    public void DynamicExpresso()
    {
        var interpreter = new Interpreter();

        interpreter.Parse(Script,
            new Parameter("x", typeof(double)),
            new Parameter("y", typeof(double)));
    }

    [Benchmark]
    public void DynamicExpressoSharedInterpreter()
    {
        _interpreter.Parse(Script,
            new Parameter("x", typeof(double)),
            new Parameter("y", typeof(double)));
    }
}
```
