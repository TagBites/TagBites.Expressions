using BenchmarkDotNet.Attributes;
using DynamicExpresso;
// ReSharper disable IdentifierTypo

namespace TagBites.Expressions.Benchmarks;

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
    public void TagBites_SharedOptions()
    {
        ExpressionParser.Parse(Script, _options);
    }

    [Benchmark]
    public void DynamicExpresso()
    {
        var interpreter = new Interpreter();

        interpreter.Parse(Script,
            new Parameter("x", typeof(double), 10),
            new Parameter("y", typeof(double), 2));
    }

    [Benchmark]
    public void DynamicExpresso_SharedInterpreter()
    {
        _interpreter.Parse(Script,
            new Parameter("x", typeof(double), 10),
            new Parameter("y", typeof(double), 2));
    }
}
