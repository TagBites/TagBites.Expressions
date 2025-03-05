# TagBites.Expressions

[![Nuget](https://img.shields.io/nuget/v/TagBites.Expressions.svg)](https://www.nuget.org/packages/TagBites.Expressions/)
[![License](https://img.shields.io/github/license/TagBites/TagBites.Expressions)](https://github.com/TagBites/TagBites.Expressions/blob/master/LICENSE)

Converts C# text expressions into LINQ expressions using **Roslyn**, supporting complete language syntax.

## Examples

```csharp
public void BasicTest()
{
    var result = ExpressionParser.Invoke("5 / 2.5");
    Assert.Equal(2d, result);

    var result2 = ExpressionParser.Invoke<int>("new [] { 1, 2, 3 }.Select(x => (x, x + 1).Item2).Sum()");
    Assert.Equal(9, result2);

    var result3 = ExpressionParser.Invoke<int>("(a + b) / 2", ("a", 2), ("b", 4));
    Assert.Equal(3, result3);
}

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

class TestModel
{
    public int X { get; set; }
    public int Y { get; set; }
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
