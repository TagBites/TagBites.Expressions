using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using DynamicExpresso;
// ReSharper disable IdentifierTypo

namespace TagBites.Expressions.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 60)]
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
    private readonly ParsingConfig _dynamicLinqConfig = new();


    [Benchmark]
    public LambdaExpression TagBites_Parse()
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(double), "x"),
                (typeof(double), "y")
            }
        };

        return ExpressionParser.Parse(Script, options);
    }

    [Benchmark]
    public LambdaExpression TagBites_Parse_SharedOptions()
    {
        return ExpressionParser.Parse(Script, _options);
    }

    [Benchmark]
    public Lambda DynamicExpresso_Parse()
    {
        var interpreter = new Interpreter();

        return interpreter.Parse(Script,
            new Parameter("x", typeof(double), 10),
            new Parameter("y", typeof(double), 2));
    }

    [Benchmark]
    public Lambda DynamicExpresso_Parse_SharedInterpreter()
    {
        return _interpreter.Parse(Script,
            new Parameter("x", typeof(double), 10),
            new Parameter("y", typeof(double), 2));
    }

    [Benchmark]
    public LambdaExpression DynamicLinqCore_Parse()
    {
        var config = new ParsingConfig();
        var parameters = new[] { Expression.Parameter(typeof(double), "x"), Expression.Parameter(typeof(double), "y") };

        return DynamicExpressionParser.ParseLambda(config, false, parameters, null, Script);
    }

    [Benchmark]
    public LambdaExpression DynamicLinqCore_Parse_SharedConfig()
    {
        var parameters = new[] { Expression.Parameter(typeof(double), "x"), Expression.Parameter(typeof(double), "y") };

        return DynamicExpressionParser.ParseLambda(_dynamicLinqConfig, false, parameters, null, Script);
    }
}
