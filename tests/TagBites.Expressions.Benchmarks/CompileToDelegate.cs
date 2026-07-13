using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using FastExpressionCompiler;
// ReSharper disable IdentifierTypo

namespace TagBites.Expressions.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 60)]
public class CompileToDelegate
{
    private const string SimpleScript = "Math.Pow(x, y) + 5";
    private const string RichScript = "x switch { < 0 => \"neg\", 0 => \"zero\", _ => new [] { x, y }.Select(v => v * 2).Sum().ToString() }";

    private readonly LambdaExpression _simpleLambda;
    private readonly LambdaExpression _richLambda;

    public CompileToDelegate()
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(double), "x"),
                (typeof(double), "y")
            }
        };

        _simpleLambda = ExpressionParser.Parse(SimpleScript, options);
        _richLambda = ExpressionParser.Parse(RichScript, options);
    }

    [Benchmark(Baseline = true)]
    public Delegate Simple_Compile() => _simpleLambda.Compile();

    [Benchmark]
    public Delegate Simple_CompileFast() => _simpleLambda.CompileFast();

    [Benchmark]
    public Delegate Rich_Compile() => _richLambda.Compile();

    [Benchmark]
    public Delegate Rich_CompileFast() => _richLambda.CompileFast();
}
