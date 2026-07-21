using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.CustomTypeProviders;
using System.Linq.Expressions;
using System.Text;
using DynamicExpresso;
// ReSharper disable IdentifierTypo

namespace TagBites.Expressions.Benchmarks;

/// <summary>
/// Compares which C# expression features each library accepts, and prints the Markdown table used in README.md
/// (section "### Comparison"). Run with the "feature-comparer" argument.
/// </summary>
/// <remarks>
/// Every feature is checked by <b>parsing, compiling, invoking and comparing the result to an expected value</b>,
/// not just by whether parsing throws.
/// </remarks>
public static class LibraryFeatureComparer
{
    private static readonly int[] s_xsValue = [1, 2, 3];
    private static readonly Meters s_mValue = new(16.0);

    /// <summary>
    /// Name is the Markdown shown in the table's "C# syntax" column.
    /// Ordered from easiest (most basic C#) to hardest.
    /// </summary>
    private static readonly (string Name, string Expr, string Expected)[] Features =
    [
        ("Arithmetic and logical operators", "1 < 2 && 2 < 3", "True"),
        ("Ternary", "1 < 2 ? 1 : 2", "1"),
        ("Member access and method calls", "\"abc\".Substring(1).Length", "2"),
        ("`is` / `as`", "(((object)\"a\") as string) is string", "True"),
        ("`typeof`, `default(T)`", "default(int) == 0 && typeof(int) == typeof(int)", "True"),
        ("Null-coalescing `??` / null-conditional `?.`", "\"abc\"?.Length ?? 0", "3"),
        ("Object and collection initializers", "new List<int> { 1, 2, 3 }.Count", "3"),
        ("Verbatim strings `@\"...\"`", "@\"a\\b\".Length", "3"),
        ("Digit separators `1_000`", "1_000_000", "1000000"),
        ("String interpolation `$\"{x,6:0.00}\"` (alignment + format)", "$\"{5,6:000}\"", "   005"),
        ("Raw string literals `\"\"\"...\"\"\"`", "\"\"\"hi\"\"\".Length", "2"),
        ("Tuples and tuple equality", "(1, 2) == (1, 2)", "True"),
        ("Anonymous objects (`new { X = 1 }`)", "new { X = 1 }.X", "1"),
        ("Null-forgiving `x!`", "\"a\"!.Length", "1"),
        ("`checked` / `unchecked`", "unchecked(2147483647 + 1)", "-2147483648"),
        ("`nameof`, `sizeof`", "nameof(xs) + sizeof(int)", "xs4"),
        ("Array creation: sized and multidimensional", "new int[,] { { 1, 2 }, { 3, 4 } }[1, 1]", "4"),
        ("Target-typed `new()`", "new List<List<int>> { new() { 1, 2 } }[0][0]", "1"),
        ("Lambdas and LINQ", "xs.Where(x => x > 1).Sum()", "5"),
        ("Generic method call with explicit type argument (`xs.OfType<int>()`)", "xs.OfType<int>().Count()", "3"),
        ("Static imports (`using static`, unqualified `Sqrt(16)`)", "Sqrt(16)", "4"),
        ("User-defined operator overloads (`DateTime.Now + TimeSpan.FromDays(1)`)", "DateTime.Now + TimeSpan.FromDays(1) > DateTime.Now", "True"),
        ("User-defined implicit/explicit conversion operators", "Math.Sqrt(m)", "4"),
        ("Switch expressions", "1 switch { 1 => 10, _ => 0 }", "10"),
        ("Pattern matching: relational, `and`/`or`/`not`, property", "\"ab\" is { Length: > 0 and < 5 }", "True"),
        ("List patterns (`arr is [1, 2, 3]`)", "xs is [1, 2, 3]", "True"),
        ("Tuple/recursive deconstruction patterns (`x is (int a, int b)`)", "(1, 2) is (int a, int b)", "True"),
    ];


    public static void Run()
    {
        var results = Features
            .Select((f, index) => (
                f.Name,
                Index: index,
                Tag: TagBites(f.Expr, f.Expected),
                Dyn: DynamicExpresso(f.Expr, f.Expected),
                Core: DynamicLinqCore(f.Expr, f.Expected)))
            .ToList();

        // Table order: most-supported first, then definition order (easiest -> hardest).
        var ordered = results
            .OrderByDescending(r => (r.Tag ? 1 : 0) + (r.Dyn ? 1 : 0) + (r.Core ? 1 : 0))
            .ThenBy(r => r.Index);

        var tagBitesVersion = FormatVersion(typeof(ExpressionParser).Assembly.GetName().Version);
        var dynamicExpressoVersion = FormatVersion(typeof(Interpreter).Assembly.GetName().Version);
        var dynamicLinqCoreVersion = FormatVersion(typeof(ParsingConfig).Assembly.GetName().Version);

        Console.WriteLine($"| C# syntax | [TagBites.Expressions](https://github.com/TagBites/TagBites.Expressions)<br>v. {tagBitesVersion} | [DynamicExpresso](https://github.com/dynamicexpresso/DynamicExpresso)<br>v. {dynamicExpressoVersion} | [System.Linq.Dynamic.Core](https://github.com/zzzprojects/System.Linq.Dynamic.Core)<br>v. {dynamicLinqCoreVersion} |");
        Console.WriteLine("|---|:---:|:---:|:---:|");
        foreach (var r in ordered)
            Console.WriteLine($"| {r.Name} | {Mark(r.Tag)} | {Mark(r.Dyn)} | {Mark(r.Core)} |");

        static string Mark(bool ok) => ok ? "✅" : "❌";
        static string FormatVersion(Version? version) => version == null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static bool TagBites(string expr, string expected)
    {
        try
        {
            var options = new ExpressionParserOptions
            {
                Parameters = { (typeof(int[]), "xs"), (typeof(Meters), "m") },
                IncludedTypes = { typeof(StringBuilder), typeof(DateTime), typeof(TimeSpan), typeof(Enumerable) },
                StaticImports = { typeof(Math) }
            };

            var result = ExpressionParser.Parse(expr, options).Compile().DynamicInvoke(s_xsValue, s_mValue);
            return Matches(result, expected);
        }
        catch { return false; }
    }
    private static bool DynamicExpresso(string expr, string expected)
    {
        try
        {
            var interpreter = new Interpreter(InterpreterOptions.Default | InterpreterOptions.LambdaExpressions);
            interpreter.Reference(typeof(StringBuilder));
            interpreter.Reference(typeof(DateTime));
            interpreter.Reference(typeof(TimeSpan));
            interpreter.Reference(typeof(Enumerable));
            interpreter.Reference(typeof(List<>));
            // No static import option

            var result = interpreter.Parse(expr, new Parameter("xs", typeof(int[])), new Parameter("m", typeof(Meters)))
                .Invoke(s_xsValue, s_mValue);
            return Matches(result, expected);
        }
        catch { return false; }
    }
    private static bool DynamicLinqCore(string expr, string expected)
    {
        try
        {
            var config = new ParsingConfig
            {
                CustomTypeProvider = new DefaultDynamicLinqCustomTypeProvider(ParsingConfig.Default, new List<Type>
                {
                    typeof(StringBuilder), typeof(DateTime), typeof(TimeSpan), typeof(Meters), typeof(List<int>), typeof(List<List<int>>)
                }, false)
            };
            // No static import option

            var xsParameter = Expression.Parameter(typeof(int[]), "xs");
            var mParameter = Expression.Parameter(typeof(Meters), "m");

            var result = DynamicExpressionParser.ParseLambda(config, false, [xsParameter, mParameter], null, expr)
                .Compile().DynamicInvoke(s_xsValue, s_mValue);
            return Matches(result, expected);
        }
        catch { return false; }
    }

    private static bool Matches(object? result, string expected)
    {
        return string.Equals(
            Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture),
            expected,
            StringComparison.Ordinal);
    }

    public struct Meters(double value)
    {
        public double Value = value;

        public static implicit operator double(Meters m) => m.Value;
        public static implicit operator Meters(double d) => new(d);
    }
}
