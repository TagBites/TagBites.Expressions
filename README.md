# TagBites.Expressions

[![Nuget](https://img.shields.io/nuget/v/TagBites.Expressions.svg)](https://www.nuget.org/packages/TagBites.Expressions/)
[![License](https://img.shields.io/github/license/TagBites/TagBites.Expressions)](https://github.com/TagBites/TagBites.Expressions/blob/master/LICENSE)

**TagBites.Expressions is a Roslyn-based C# expression parser and evaluator for .NET.**
It compiles runtime string expressions into strongly typed `Func<>` delegates or `LambdaExpression` expression trees, without creating a new assembly.

```csharp
var options = new ExpressionParserOptions { Parameters = { (typeof(int), "a"), (typeof(int), "b") } };
var func = ExpressionParser.Compile<Func<int, int, int>>("(a + b) / 2", options);
int r = func(2, 4); // 3
```

Because Roslyn does the parsing, expressions use real C# syntax: operators, precedence, numeric promotion, implicit conversions, pattern matching, tuples, lambdas, LINQ and generics behave like they do in the C# compiler. If C# accepts the expression, TagBites.Expressions accepts it; if C# rejects it, so does the parser.

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

String interpolation, including alignment and format specifiers (formatting follows the current culture):

```csharp
ExpressionParser.Invoke(@"$""sum = {1 + 2}""");                          // sum = 3
ExpressionParser.Invoke(@"$""{5,-4}|""");                                // "5   |"   (left aligned)
ExpressionParser.Invoke(@"$""{5,6:000}""");                              // "   005"  (alignment + format)
ExpressionParser.Invoke(@"$""{255:X}""");                                // FF
ExpressionParser.Invoke(@"$""{new DateTime(2021, 8, 14):yyyy-MM-dd}"""); // 2021-08-14
ExpressionParser.Invoke(@"$""{(1 < 2 ? ""yes"" : ""no"")}""");           // yes
```

Get the expression tree, or parse without throwing:

```csharp
LambdaExpression lambda = ExpressionParser.Parse("x * 2 + 1", options);

if (!ExpressionParser.TryParse("a + ", options, out var expr, out var error))
    Console.WriteLine(error);
```
## Use cases

Use TagBites.Expressions when you need to parse, validate, evaluate or compile C# expressions from strings at runtime:
- dynamic business rules and predicates
- user-defined formulas and calculations
- configurable filters and scoring logic
- compile-once/run-many `Func<>` delegates
- `LambdaExpression` trees for expression-based APIs
- LINQ-style runtime logic with real C# expression syntax

## Why TagBites.Expressions?

- **Real C# expression syntax** — parsed by Roslyn, not by a custom C#-like grammar.
- **Runtime expression evaluation** — evaluate once or compile once and invoke many times.
- **Delegates or expression trees** — compile to `Func<>` delegates or parse to `LambdaExpression`.
- **Modern C# expressions** — supports LINQ, lambdas, pattern matching, switch expressions, tuples, generics, interpolated strings, etc.
- **No generated assembly** — expressions are compiled without creating a new assembly.

## Supported C# expression syntax


- Operators: arithmetic, bitwise, shifts, comparison, `&& || !`, `?:`, `??`, `?.`/`?[]`, `is`/`as`, `x!`.
- Literals: all numeric types, `char`, `string`, verbatim and interpolated strings, hex, digit separators.
- Members and calls: properties, fields, indexers (including index-from-end `x[^1]`), generic and extension methods, `params`.
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

TagBites.Expressions fits between lightweight expression evaluators and full C# scripting engines: it supports real C# expression syntax through Roslyn, returns delegates or expression trees, and avoids generating a new assembly.

| | TagBites.Expressions | DynamicExpresso | System.Linq.Dynamic.Core | Roslyn scripting (`CSharpScript`) |
|---|---|---|---|---|
| Language | C# expressions (Roslyn) | C#-like (own parser) | Dynamic LINQ dialect | Full C# (official) |
| Output | Delegate / Expression | Delegate / Expression | Expression tree | Compiled assembly |
| Startup / memory | Low | Low | Low | High |
| Dependency | Roslyn | None | None | Roslyn |

### C# syntax vs DynamicExpresso

Because TagBites parses with Roslyn, it accepts modern C# syntax that DynamicExpresso's own parser does not.  
Verified against DynamicExpresso 2.19.3 (see [LibraryFeatureComparer.cs](https://github.com/TagBites/TagBites.Expressions/blob/master/tests/TagBites.Expressions.Benchmarks/LibraryFeatureComparer.cs)):

| C# syntax | TagBites | DynamicExpresso |
|---|:---:|:---:|
| String interpolation `$"{x,6:0.00}"` (alignment + format) | ✓ | ✗ |
| Switch expressions | ✓ | ✗ |
| Pattern matching: relational, `and`/`or`/`not`, property | ✓ | ✗ |
| Tuples and tuple equality | ✓ | ✗ |
| Array creation: sized and multidimensional | ✓ | ✗ |
| `checked` / `unchecked` | ✓ | ✗ |
| `nameof`, `sizeof` | ✓ | ✗ |
| Null-forgiving `x!` | ✓ | ✗ |
| Verbatim strings `@"..."` | ✓ | ✗ |
| Digit separators `1_000` | ✓ | ✗ |

Both handle arithmetic and logical operators, member access, method calls, generics, lambdas and LINQ, `is`/`as`, `typeof`, `default(T)`, object initializers, ternary and null-coalescing/-conditional.

#### Benchmark

Parsing `"Math.Pow(x, y) + 5"` into a LINQ expression. TagBites (v. 1.1.0) vs DynamicExpresso (v. 2.19.3).

| Method                                  | Mean      | Error     | StdDev    | Allocated |
|---------------------------------------- |----------:|----------:|----------:|----------:|
| TagBites_Parse                          |  8.198 us | 0.1567 us | 0.3338 us |   6.85 KB |
| TagBites_Parse_SharedOptions            |  8.218 us | 0.1502 us | 0.3233 us |   6.51 KB |
| DynamicExpresso_Parse                   | 26.280 us | 0.7047 us | 1.5017 us |  30.75 KB |
| DynamicExpresso_Parse_SharedInterpreter | 13.428 us | 0.3094 us | 0.6390 us |  12.32 KB |

Benchmark source: [ParseToExpression.cs](https://github.com/TagBites/TagBites.Expressions/blob/master/tests/TagBites.Expressions.Benchmarks/ParseToExpression.cs).
