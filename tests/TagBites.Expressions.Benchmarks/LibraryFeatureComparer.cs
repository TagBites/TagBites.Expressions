using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.CustomTypeProviders;
using System.Linq.Expressions;
using System.Text;
using DynamicExpresso;
// ReSharper disable IdentifierTypo

namespace TagBites.Expressions.Benchmarks;

public static class LibraryFeatureComparer
{
    public static void Run()
    {
        var features = new (string Name, string Expr)[]
        {
            ("math", "1 + 2"),
            ("String interpolation", "$\"{1 + 2}\""),
            ("Interpolation align/format", "$\"{5,6:000}\""),
            ("Switch expression", "1 switch { 1 => 10, _ => 0 }"),
            ("Pattern: type", "((object)1) is int"),
            ("Pattern: relational", "5 is > 3"),
            ("Pattern: and/or/not", "5 is > 0 and < 10"),
            ("Pattern: property", "\"ab\" is { Length: 2 }"),
            ("Tuples", "(1, 2).Item2"),
            ("Tuple equality", "(1, 2) == (1, 2)"),
            ("Tuple pattern", "(1, 2) is (int a, int b)"),
            ("List pattern", "xs is [1, 2, 3]"),
            ("Multidim array create", "new int[,] { { 1, 2 }, { 3, 4 } }[1, 1]"),
            ("Sized array create", "new int[3].Length"),
            ("Object initializer", "new StringBuilder { Capacity = 16 }.Capacity"),
            ("Collection initializer", "new List<int> { 1, 2, 3 }.Count"),
            ("Target-typed new()", "new List<List<int>> { new() { 1, 2 } }[0][0]"),
            ("Anonymous object", "new { X = 1 }.X"),
            ("checked/unchecked", "unchecked(2147483647 + 1)"),
            ("nameof", "nameof(xs)"),
            ("sizeof", "sizeof(int)"),
            ("default(T)", "default(int)"),
            ("typeof", "typeof(int)"),
            ("Null-forgiving x!", "\"a\"!.Length"),
            ("Verbatim string", "@\"a\\b\".Length"),
            ("Digit separators", "1_000_000"),
            ("Is operator", "1 is int"),
            ("As operator", "((object)\"a\") as string"),
            ("Arithmetic/logical operators", "1 < 2 && 2 < 3"),
            ("Member access/method calls", "\"abc\".Substring(1).Length"),
            ("Lambda (LINQ)", "xs.Where(x => x > 1).Sum()"),
            ("Generic method call", "xs.OfType<int>().Count()"),
            ("Ternary", "1 < 2 ? 1 : 2"),
            ("Null-coalescing", "((int?)null) ?? 5"),
            ("Null-conditional", "\"a\"?.Length"),
        };

        Console.WriteLine($"| {"Feature",-28} | TagBites | DynamicExpresso | System.Linq.Dynamic.Core |");
        Console.WriteLine($"|{new string('-', 30)}|----------|-----------------|--------------------------|");
        foreach (var (name, expr) in features)
        {
            var t = TagBites(expr) ? "yes" : "NO";
            var d = DynamicExpresso(expr) ? "yes" : "NO";
            var c = DynamicLinqCore(expr) ? "yes" : "NO";
            Console.WriteLine($"| {name,-28} | {t,-8} | {d,-15} | {c,-24} |");
        }
    }

    private static bool TagBites(string expr)
    {
        try
        {
            var options = new ExpressionParserOptions
            {
                Parameters = { (typeof(int[]), "xs") },
                IncludedTypes = { typeof(StringBuilder), typeof(DateTime), typeof(Enumerable) }
            };
            ExpressionParser.Parse(expr, options);
            return true;
        }
        catch { return false; }
    }
    private static bool DynamicExpresso(string expr)
    {
        try
        {
            var interpreter = new Interpreter(InterpreterOptions.Default | InterpreterOptions.LambdaExpressions);
            interpreter.Reference(typeof(StringBuilder));
            interpreter.Reference(typeof(DateTime));
            interpreter.Reference(typeof(Enumerable));
            interpreter.Reference(typeof(List<>));
            interpreter.Parse(expr, new Parameter("xs", typeof(int[])));
            return true;
        }
        catch { return false; }
    }
    private static bool DynamicLinqCore(string expr)
    {
        try
        {
            var config = new ParsingConfig
            {
                CustomTypeProvider = new DefaultDynamicLinqCustomTypeProvider(ParsingConfig.Default, new List<Type>
                {
                    typeof(StringBuilder), typeof(DateTime), typeof(List<int>), typeof(List<List<int>>)
                }, false)
            };
            var parameter = Expression.Parameter(typeof(int[]), "xs");
            DynamicExpressionParser.ParseLambda(config, false, [parameter], null, expr);
            return true;
        }
        catch { return false; }
    }
}
