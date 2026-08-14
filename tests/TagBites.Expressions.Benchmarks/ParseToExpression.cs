using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using DynamicExpresso;
// ReSharper disable IdentifierTypo
#pragma warning disable CA1822

namespace TagBites.Expressions.Benchmarks;

[MemoryDiagnoser]
public class ParseToExpression
{
    private const string Script = "Math.Pow(x, y) + 5";
    private const string LambdaScript = "list.Where(x => x > limit).Select(x => Math.Pow(x, y)).Sum()";
    private const string CallsScript = "name.Trim().ToUpper().Length + Math.Round(total, 2)";

    private readonly IList<int> _list = new List<int> { 1, 2, 3, 4 };

    private readonly ExpressionParserOptions _options = CreateTagBitesParseOptions(true);
    private readonly ExpressionParserOptions _lambdaOptions = CreateTagBitesParseLambdaOptions(true);
    private readonly ExpressionParserOptions _callsOptions = CreateTagBitesParseCallsOptions(true);

    private readonly Interpreter _interpreter = CreateDynamicExpressoInterpreter();
    private readonly Interpreter _lambdaInterpreter = CreateDynamicExpressoLambdaInterpreter();
    private readonly Interpreter _callsInterpreter = CreateDynamicExpressoInterpreter();

    private readonly ParsingConfig _dynamicLinqConfig = new();


    [Benchmark]
    [BenchmarkCategory("Parse")]
    public LambdaExpression TagBites_Parse()
    {
        var options = CreateTagBitesParseOptions();
        return ExpressionParser.Parse(Script, options);
    }
    [Benchmark]
    [BenchmarkCategory("Parse", "Shared")]
    public LambdaExpression TagBites_Parse_SharedEnv()
    {
        return ExpressionParser.Parse(Script, _options);
    }
    private static ExpressionParserOptions CreateTagBitesParseOptions(bool useCache = false)
    {
        return new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(double), "x"),
                (typeof(double), "y")
            },
            UseMemberCache = useCache
        };
    }

    [Benchmark]
    [BenchmarkCategory("Lambda")]
    public LambdaExpression TagBites_ParseLambda()
    {
        var options = CreateTagBitesParseLambdaOptions();
        return ExpressionParser.Parse(LambdaScript, options);
    }
    [Benchmark]
    [BenchmarkCategory("Lambda", "Shared")]
    public LambdaExpression TagBites_ParseLambda_SharedEnv()
    {
        return ExpressionParser.Parse(LambdaScript, _lambdaOptions);
    }
    private static ExpressionParserOptions CreateTagBitesParseLambdaOptions(bool useCache = false)
    {
        return new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(IList<int>), "list"),
                (typeof(int), "limit"),
                (typeof(double), "y")
            },
            UseMemberCache = useCache
        };
    }

    [Benchmark]
    [BenchmarkCategory("Calls")]
    public LambdaExpression TagBites_ParseCalls()
    {
        var options = CreateTagBitesParseCallsOptions();
        return ExpressionParser.Parse(CallsScript, options);
    }
    [Benchmark]
    [BenchmarkCategory("Calls", "Shared")]
    public LambdaExpression TagBites_ParseCalls_SharedEnv()
    {
        return ExpressionParser.Parse(CallsScript, _callsOptions);
    }
    private static ExpressionParserOptions CreateTagBitesParseCallsOptions(bool useCache = false)
    {
        return new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(string), "name"),
                (typeof(decimal), "total")
            },
            UseMemberCache = useCache
        };
    }

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public Lambda DynamicExpresso_Parse()
    {
        var interpreter = CreateDynamicExpressoInterpreter();
        return interpreter.Parse(Script, CreateDynamicExpressoParameters());
    }
    [Benchmark]
    [BenchmarkCategory("Parse", "Shared")]
    public Lambda DynamicExpresso_Parse_SharedEnv()
    {
        return _interpreter.Parse(Script, CreateDynamicExpressoParameters());
    }
    private static Interpreter CreateDynamicExpressoInterpreter()
    {
        return new Interpreter();
    }
    private Parameter[] CreateDynamicExpressoParameters()
    {
        return [new Parameter("x", typeof(double), 10),
                new Parameter("y", typeof(double), 2)];
    }

    [Benchmark]
    public Lambda DynamicExpresso_ParseLambda()
    {
        var interpreter = CreateDynamicExpressoLambdaInterpreter();
        return interpreter.Parse(LambdaScript, CreateDynamicExpressoLambdaParameters());
    }
    [BenchmarkCategory("Lambda")]
    [Benchmark]
    [BenchmarkCategory("Lambda", "Shared")]
    public Lambda DynamicExpresso_ParseLambda_SharedEnv()
    {
        return _lambdaInterpreter.Parse(LambdaScript, CreateDynamicExpressoLambdaParameters());
    }
    private static Interpreter CreateDynamicExpressoLambdaInterpreter()
    {
        return new Interpreter(InterpreterOptions.Default | InterpreterOptions.LambdaExpressions);
    }
    private Parameter[] CreateDynamicExpressoLambdaParameters()
    {
        return [new Parameter("list", typeof(IList<int>), _list),
                new Parameter("limit", typeof(int), 2),
                new Parameter("y", typeof(double), 2)];
    }

    [Benchmark]
    [BenchmarkCategory("Calls")]
    public Lambda DynamicExpresso_ParseCalls()
    {
        var interpreter = CreateDynamicExpressoInterpreter();
        return interpreter.Parse(CallsScript, CreateDynamicExpressoCallsParameters());
    }
    [Benchmark]
    [BenchmarkCategory("Calls", "Shared")]
    public Lambda DynamicExpresso_ParseCalls_SharedEnv()
    {
        return _callsInterpreter.Parse(CallsScript, CreateDynamicExpressoCallsParameters());
    }
    private Parameter[] CreateDynamicExpressoCallsParameters()
    {
        return [new Parameter("name", typeof(string), "abc"),
                new Parameter("total", typeof(decimal), 1234.567m)];
    }

    [Benchmark]
    [BenchmarkCategory("Parse")]
    public LambdaExpression DynamicLinqCore_Parse()
    {
        var config = new ParsingConfig();
        return DynamicExpressionParser.ParseLambda(config, false, CreateDynamicLinqCoreParameters(), null, Script);
    }
    [Benchmark]
    [BenchmarkCategory("Parse", "Shared")]
    public LambdaExpression DynamicLinqCore_Parse_SharedEnv()
    {
        return DynamicExpressionParser.ParseLambda(_dynamicLinqConfig, false, CreateDynamicLinqCoreParameters(), null, Script);
    }
    private static ParameterExpression[] CreateDynamicLinqCoreParameters()
    {
        return [Expression.Parameter(typeof(double), "x"), Expression.Parameter(typeof(double), "y")];
    }

    [Benchmark]
    [BenchmarkCategory("Lambda")]
    public LambdaExpression DynamicLinqCore_ParseLambda()
    {
        var config = new ParsingConfig();
        return DynamicExpressionParser.ParseLambda(config, false, CreateDynamicLinqCoreLambdaParameters(), null, LambdaScript);
    }
    [Benchmark]
    [BenchmarkCategory("Lambda", "Shared")]
    public LambdaExpression DynamicLinqCore_ParseLambda_SharedEnv()
    {
        return DynamicExpressionParser.ParseLambda(_dynamicLinqConfig, false, CreateDynamicLinqCoreLambdaParameters(), null, LambdaScript);
    }
    private static ParameterExpression[] CreateDynamicLinqCoreLambdaParameters()
    {
        return [Expression.Parameter(typeof(IList<int>), "list"), Expression.Parameter(typeof(int), "limit"), Expression.Parameter(typeof(double), "y")];
    }

    [Benchmark]
    [BenchmarkCategory("Calls")]
    public LambdaExpression DynamicLinqCore_ParseCalls()
    {
        var config = new ParsingConfig();
        return DynamicExpressionParser.ParseLambda(config, false, CreateDynamicLinqCoreCallsParameters(), null, CallsScript);
    }
    [Benchmark]
    [BenchmarkCategory("Calls", "Shared")]
    public LambdaExpression DynamicLinqCore_ParseCalls_SharedEnv()
    {
        return DynamicExpressionParser.ParseLambda(_dynamicLinqConfig, false, CreateDynamicLinqCoreCallsParameters(), null, CallsScript);
    }
    private static ParameterExpression[] CreateDynamicLinqCoreCallsParameters()
    {
        return [Expression.Parameter(typeof(string), "name"), Expression.Parameter(typeof(decimal), "total")];
    }
}
