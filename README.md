# TagBites.Expressions

[![Nuget](https://img.shields.io/nuget/v/TagBites.Expressions.svg)](https://www.nuget.org/packages/TagBites.Expressions/)
[![License](https://img.shields.io/github/license/TagBites/TagBites.Expressions)](https://github.com/TagBites/TagBites.Expressions/blob/master/LICENSE)

Compile C# text expressions into native .NET delegates using **Roslyn**, without creating a new assembly.

```csharp
var options = new ExpressionParserOptions { Parameters = { (typeof(int), "a"), (typeof(int), "b") } };
var func = ExpressionParser.Compile<Func<int, int, int>>("(a + b) / 2", options);
int r = func(2, 4); // 3
```

Because Roslyn does the parsing, the grammar is real C#: operators, precedence, numeric promotion, implicit conversions, `checked`/`unchecked`, pattern matching, tuples, lambdas and generics behave as the compiler does. If it compiles in C#, it runs here; if C# rejects it, so does the parser.

## Install

```
dotnet add package TagBites.Expressions
```

Targets `netstandard2.0`. Only dependency is `Microsoft.CodeAnalysis.CSharp`.

## Usage

Evaluate once:

```csharp
ExpressionParser.Invoke("5 / 2.5");                              // 2d
ExpressionParser.Invoke<int>("new [] { 1, 2, 3 }.Sum()");        // 6
ExpressionParser.Invoke<int>("(a + b) / 2", ("a", 2), ("b", 4)); // 3
```

Compile once, run many times:

```csharp
var options = new ExpressionParserOptions { Parameters = { (typeof(double), "x"), (typeof(double), "y") } };
var func = ExpressionParser.Compile<Func<double, double, double>>("Math.Pow(x, y) + 5", options);
func(2, 10); // 1029
func(2, 2);  // 9
```

Bind an object as `this`:

```csharp
var options = new ExpressionParserOptions
{
    Parameters = { (typeof(TestModel), "this") },
    UseFirstParameterAsThis = true
};

ExpressionParser.Invoke("X + Y", options, new TestModel { X = 1, Y = 2 }); // 3
```

Expose named values and delegates with `GlobalMembers`:

```csharp
var options = new ExpressionParserOptions
{
    Parameters = { (typeof(int), "a") },
    GlobalMembers = { { "b", (null, 2) } }
};

var func = ExpressionParser.Compile<Func<int, int>>("a switch { 1 => b, 2 => b * 2, _ => b + a }", options);
func(3); // 5
```

Get the expression tree, or parse without throwing:

```csharp
LambdaExpression lambda = ExpressionParser.Parse("x * 2 + 1", options);

if (!ExpressionParser.TryParse("a + ", options, out var expr, out var error))
    Console.WriteLine(error);
```

## Supported syntax

The whole C# expression grammar.

- Operators: arithmetic, bitwise, shifts, comparison, `&& || !`, `?:`, `??`, `?.`/`?[]`, `is`/`as`, `x!`.
- Literals: all numeric types, `char`, `string`, verbatim and interpolated strings, hex, digit separators.
- Members and calls: properties, fields, indexers, generic and extension methods, `params`.
- `new`: constructors, object initializers, arrays (jagged, multidimensional and sized), generic collections.
- Lambdas and LINQ (`Select`, `Where`, `GroupBy`, ...), including nested and multi-argument lambdas.
- Tuples, including element-wise equality.
- `typeof`, `default(T)`, `nameof`, `sizeof`, `checked`, `unchecked`.
- Pattern matching in `is` and `switch`: type, constant, relational, `and`/`or`/`not`, property, positional and
  `var` patterns, `when` guards.

Statements, `async`/`await` and type declarations are out of scope.

## Configuration

`ExpressionParserOptions`:

| Option | Purpose |
|---|---|
| `Parameters` | Typed parameters of the resulting lambda. |
| `GlobalMembers` | Named values and delegates usable by name; a member named `this` is implicit. |
| `UseFirstParameterAsThis` | Use the first parameter as `this` so its members need no prefix. |
| `IncludedTypes` | Types (and static classes) an expression may reference by name. |
| `CustomPropertyResolver` | Resolve members at runtime, e.g. against types defined only at runtime. |
| `ResultType` | Require the result to be this type. An implicit conversion is applied if needed, otherwise parsing fails. |
| `ResultCastType` | Force the result to this type with an explicit cast, e.g. to compile every expression as `Func<object>`. |
| `AllowReflection` | Allow reflection APIs. (default: `false`) |
| `AllowRuntimeCast` | Allow custom keywords `typeis` / `typeas` / `typecast` against runtime type names. (default: `false`) |

**Result type:**  
`ResultType` is a contract: the expression must produce this type. A C# implicit conversion (like `int` -> `long`) is applied automatically; anything else is a parse error. Use it to require, for example, that a filter is a `bool`.  
`ResultCastType` forces the return type with an explicit cast, so unrelated expressions can share one delegate signature. It also allows casts that are not implicit, such as `double` -> `int`.

The two combine: to run many rules through a single `Func<object>` while still requiring each to be boolean, set `ResultType = typeof(bool)` (reject anything non-boolean) together with `ResultCastType = typeof(object)`.

## Alternatives

| | TagBites.Expressions | DynamicExpresso | System.Linq.Dynamic.Core | Roslyn scripting (`CSharpScript`) |
|---|---|---|---|---|
| Language | C# expressions (Roslyn) | C#-like (own parser) | Dynamic LINQ dialect | Full C# (official) |
| Output | Delegate / Expression | Delegate / Expression | Expression tree | Compiled assembly |
| Startup / memory | Low | Low | Low | High |
| Dependency | Roslyn | None | None | Roslyn |

## Benchmark

Parsing `"Math.Pow(x, y) + 5"` into a LINQ expression. TagBites (v. 1.1.0) vs DynamicExpresso (v. 2.19.3).

| Method                                  | Mean      | Error     | StdDev    | Allocated |
|---------------------------------------- |----------:|----------:|----------:|----------:|
| TagBites_Parse                          |  8.198 us | 0.1567 us | 0.3338 us |   6.85 KB |
| TagBites_Parse_SharedOptions            |  8.218 us | 0.1502 us | 0.3233 us |   6.51 KB |
| DynamicExpresso_Parse                   | 26.280 us | 0.7047 us | 1.5017 us |  30.75 KB |
| DynamicExpresso_Parse_SharedInterpreter | 13.428 us | 0.3094 us | 0.6390 us |  12.32 KB |

Benchmark source: [ParseToExpression.cs](https://github.com/TagBites/TagBites.Expressions/blob/master/tests/TagBites.Expressions.Benchmarks/ParseToExpression.cs).
