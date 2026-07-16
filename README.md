# TagBites.Expressions

[![Nuget](https://img.shields.io/nuget/v/TagBites.Expressions.svg)](https://www.nuget.org/packages/TagBites.Expressions/)
![.NET Standard 2.0](https://img.shields.io/badge/.NET%20Standard-2.0-512BD4)
[![License](https://img.shields.io/github/license/TagBites/TagBites.Expressions)](https://github.com/TagBites/TagBites.Expressions/blob/master/LICENSE)
[![Build](https://img.shields.io/github/actions/workflow/status/TagBites/TagBites.Expressions/build-and-test.yml?branch=master)](https://github.com/TagBites/TagBites.Expressions/actions/workflows/build-and-test.yml)
[![Downloads](https://img.shields.io/nuget/dt/TagBites.Expressions.svg)](https://www.nuget.org/packages/TagBites.Expressions/)

**TagBites.Expressions is a Roslyn-based C# expression parser and evaluator for .NET.**
It compiles runtime string expressions into strongly typed `Func<>` delegates or `LambdaExpression` expression trees, without creating a new assembly.

```csharp
var options = new ExpressionParserOptions { Parameters = { (typeof(int), "a"), (typeof(int), "b") } };
var func = ExpressionParser.Compile<Func<int, int, int>>("(a + b) / 2", options);
int r = func(2, 4); // 3
```

Because Roslyn does the parsing, expressions use real C# syntax: operators, precedence, numeric promotion, implicit conversions, pattern matching, tuples, lambdas, LINQ and generics behave like they do in the C# compiler. If C# accepts the expression, TagBites.Expressions accepts it; if C# rejects it, so does the parser.

[Try it online](https://tagbites.github.io/TagBites.Expressions/) - type an expression and evaluate it in the browser.

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

Anonymous objects (`new { ... }`) **work like a real anonymous type without generating a new one, by internally mapping it to `DynamicObject`**:

```csharp
var script = "new[] { 1, 2, 3 }.Select(v => new { Value = v, Doubled = v * 2 }).Sum(v => v.Value + v.Doubled)";
dynamic result = ExpressionParser.Invoke(script);
Console.WriteLine(result); // 18
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

- **Real C# expression syntax** - parsed by Roslyn, not by a custom C#-like grammar.
- **Runtime expression evaluation** - evaluate once or compile once and invoke many times.
- **Delegates or expression trees** - compile to `Func<>` delegates or parse to `LambdaExpression`.
- **Modern C# expressions** - supports LINQ, lambdas, pattern matching, switch expressions, tuples, generics, interpolated strings, etc.
- **No generated assembly** - expressions are compiled without creating a new assembly.

## Supported C# expression syntax


- Operators: arithmetic, bitwise, shifts, comparison, `&& || !`, `?:`, `??`, `?.`/`?[]`, `is`/`as`, `x!`.
- Literals: all numeric types, `char`, `string`, verbatim and interpolated strings, hex, digit separators.
- Members and calls: properties, fields, indexers (including index-from-end `x[^1]`), generic and extension methods, `params`.
- `new`: constructors, object and collection initializers, arrays (jagged, multidimensional and sized), target-typed `new()`.
- Anonymous objects (`new { X = 1, Y = 2 }` - see Usage above).
- Lambdas and LINQ (`Select`, `Where`, `GroupBy`, ...), including nested and multi-argument lambdas.
- Tuples, including element-wise equality.
- `typeof`, `default(T)`, `nameof`, `sizeof`, `checked`, `unchecked`.
- Pattern matching in `is` and `switch`: type, constant, relational, `and`/`or`/`not`, property, positional and
  `var` patterns, `when` guards.

Statements (like `if`), `async`/`await`, and declarations (methods, types) are out of scope - this is an expression parser.

Not currently supported:
- The range operator (`1..2`, `arr[1..^1]`).
- Target-typed `new()` as a method call argument (`obj.Method(new())`) - use an explicit type there for now.

## Configuration

`ExpressionParserOptions`:

| Option | Purpose |
|---|---|
| `AllowReflection` | Allow reflection APIs. (default: `false`) |
| `Parameters` | Typed parameters of the resulting lambda. |
| `UseFirstParameterAsThis` | Use the first parameter as `this` so its members need no prefix. |
| `GlobalMembers` | Named values and delegates usable by name; a member named `this` is implicit. |
| `IncludedTypes` | Types (and static classes) an expression may reference by name. |
| `CustomPropertyResolver` | Resolve members at runtime, e.g. against types defined only at runtime. |
| `ResultType` | Require the result to be this type. An implicit conversion is applied if needed, otherwise parsing fails. |
| `ResultCastType` | Force the result to this type with an explicit cast, e.g. to compile every expression as `Func<object>`. |
| `UseMemberCache` | Caches reflected members (methods, indexers, extension methods) on this options instance. |

**CustomPropertyResolver**:  
It is only called for `instance.Member`, it needs an instance to work on. That can be an ordinary parameter, accessed explicitly - `p.Age` works for any parameter name. A bare name like `Age` also works, but it is then resolved implicitly as `this.Age`, so a `this` must be set up first: `UseFirstParameterAsThis`, or a `this` entry in `GlobalMembers`.

**Result type:**  
`ResultType` is a contract: the expression must produce this type. A C# implicit conversion (like `int` -> `long`) is applied automatically; anything else is a parse error. Use it to require, for example, that a filter is a `bool`.  
`ResultCastType` forces the return type with an explicit cast, so unrelated expressions can share one delegate signature. It also allows casts that are not implicit, such as `double` -> `int`.

The two combine: to run many rules through a single `Func<object>` while still requiring each to be boolean, set `ResultType = typeof(bool)` (reject anything non-boolean) together with `ResultCastType = typeof(object)`.

### Non-standard options
These opt-in options (all default to `false`) make the parser accept syntax or semantics that real C# does not:

| Option | Purpose |
|---|---|
| `AllowStringRelationalOperators` | Allow `<` / `<=` / `>` / `>=` on strings, compared ordinally via `string.Compare` - not valid in real C#. |
| `AllowRuntimeCast` | Allow custom keywords `typeis` / `typeas` / `typecast` against runtime type names. |

## Advanced usage

### FastExpressionCompiler

`ExpressionParser.Parse()` returns a plain `LambdaExpression`, so it can be compiled with any compiler instead of the built-in `Compile()`. [FastExpressionCompiler](https://github.com/dadhi/FastExpressionCompiler) is a drop-in, dependency-free replacement for `LambdaExpression.Compile()` that produces the same delegate much faster:

```
dotnet add package FastExpressionCompiler
```

```csharp
using FastExpressionCompiler;

var lambda = ExpressionParser.Parse("Math.Pow(x, y) + 5", options);
var func = (Func<double, double, double>)lambda.CompileFast();
```

#### Benchmark

| Expression | `Compile()` | `CompileFast()` | Speedup |
|---|---:|---:|---:|
| `Math.Pow(x, y) + 5` | 28.23 µs | 2.24 µs | ~12.6x |
| `x switch { ... }` with LINQ `Select`/`Sum` | 180.92 µs | 5.98 µs | ~30x |


The more complex the expression tree, the bigger the gap, since most of the reflection-emit overhead `Compile()` pays per node is avoided by `CompileFast()`. 

Benchmark source: [CompileToDelegate.cs](https://github.com/TagBites/TagBites.Expressions/blob/master/tests/TagBites.Expressions.Benchmarks/CompileToDelegate.cs).

### Dynamic / Runtime-defined types

`CustomPropertyResolver` lets an expression navigate types whose shape only exists at runtime - a database row, a CMS content type, a value that lives in another process. 

The pattern:
1. Represent every runtime-shaped value with **one real, fixed .NET type** (not `object`, not a type generated per shape). Keep the actual field names/types in a separate schema object. (in example: Value/Instance = `DynamicRecord`, ValueType = `DynamicRecordSchema`)
2. In `CustomPropertyResolver`, look up the requested member by name against that schema, and build a call to read it.
3. Attach the schema to the *result* with `context.IncludeTypeInfo(expression, schema)`, so a later `.Member` further down the chain can retrieve it again through `context.InstanceTypeInfo`.

```csharp
// Schema (DynamicRecordSchema and DynamicRecord are example types)
var personSchema = new TypeSchema(new Dictionary<string, TypeSchema> { ["Name"] = new(typeof(string)), ["Age"] = new(typeof(int)) });
var rootSchema = new TypeSchema(new Dictionary<string, TypeSchema> { ["People"] = new("Person", true) });
var dataSourceSchema = new DynamicRecordSchema { ["Person"] = personSchema, ["this"] = rootSchema };

// Source
var alice = new DynamicRecord { ["Name"] = "Alice", ["Age"] = 30 };
var root = new DynamicRecord { ["People"] = new List<DynamicRecord> { alice } };

// Parse
var options = new ExpressionParserOptions
{
    Parameters = { (typeof(DynamicRecord), "this") },
    UseFirstParameterAsThis = true,
    CustomPropertyResolver = x => Resolver(dataSourceSchema, x)
};
var expression = "People.Where(p => p.Age > 18).Select(x => x.Name).First()";
var result = ExpressionParser.Invoke<string>(expression, options, root); // Alice

// Resolver
Expression? Resolver(DynamicRecordSchema dataSourceSchema, IExpressionMemberResolverContext context)
{
    if (context.Instance.Type != typeof(DynamicRecord))
        return null;

    // Member type
    var instanceSchema = context.InstanceTypeInfo as TypeSchema
        ?? (context.MemberFullPath == "this." + context.MemberName ? dataSourceSchema.GetValueOrDefault("this") : null);
    if (instanceSchema == null)
        return null;

    if (instanceSchema.Fields == null && instanceSchema.Name != null)
    {
        instanceSchema = dataSourceSchema.GetValueOrDefault(instanceSchema.Name);
        if (instanceSchema == null)
            return null;
    }

    // Value
    if (instanceSchema.Fields?.TryGetValue(context.MemberName, out var fieldTypeScheme) != true)
        return null;

    var fieldType = fieldTypeScheme!.Type;
    var isKnownType = fieldType != null;
    if (fieldType == null)
    {
        fieldType = typeof(DynamicRecord);
        if (fieldTypeScheme.IsCollection)
            fieldType = typeof(IList<DynamicRecord>);
    }

    var method = typeof(DynamicRecord).GetMethod(nameof(DynamicRecord.GetValue))!.MakeGenericMethod(fieldType);
    var result = Expression.Call(context.Instance, method, Expression.Constant(context.MemberName));

    return !isKnownType
        ? context.IncludeTypeInfo(result, fieldTypeScheme) // Wrap expression to include a type info
        : result;
}

// Sample value and schema types
class DynamicRecord : Dictionary<string, object>
{
    public T GetValue<T>(string name) => TryGetValue(name, out var value) && value is T v ? v : default;
}
class DynamicRecordSchema : Dictionary<string, TypeSchema>;
class TypeSchema { /* ... */ }
```

**LINQ over a dynamic collection works without any extra code**, as long as the collection itself is exposed as a real, closed type - `IEnumerable<DynamicRecord>`. Because it's a real type, extensions like `Where` or `Select`, `Sum`, `Count` resolve as ordinary LINQ extension methods. `CustomPropertyResolver` never has to intercept a method call, only plain member access. And the element parameter of a lambda passed to one of those methods automatically inherits the collection's `InstanceTypeInfo`, so it's correctly typed too.

> Parameters and global members have no type info, so every "dynamic" object must by resolved by resolver.

> `People` resolves through `CustomPropertyResolver` and is tagged via `context.IncludeTypeInfo(call, PersonSchema)`. Inside the lambda, `p` "knows" it's a `Person` too, because parser extracts that same type info from the collection and applies it to `p`, so `p.Age` is resolved by the very same resolver branch that resolved `People`.  
> Type info is propagated through method chains only for collections. The receiver has to be an `IEnumerable<X>`, and the info flows to a result that keeps the same element type: another `IEnumerable<X>` (`Where`, `Select`, `OrderBy`, ...) or a single `X` (`First`, `FirstOrDefault`, ...). That's why `People.Where(...).First().Name` still knows the element is a `Person`.

Full example: [CustomPropertyResolverTests.cs](https://github.com/TagBites/TagBites.Expressions/blob/master/tests/TagBites.Expressions.Tests/CustomPropertyResolverTests.cs).

## Alternatives

TagBites.Expressions fits between lightweight expression evaluators and full C# scripting engines: it supports real C# expression syntax through Roslyn, returns delegates or expression trees, and avoids generating a new assembly.

| | [TagBites.Expressions](https://github.com/TagBites/TagBites.Expressions) | [DynamicExpresso](https://github.com/dynamicexpresso/DynamicExpresso) | [System.Linq.Dynamic.Core](https://github.com/zzzprojects/System.Linq.Dynamic.Core) | [Roslyn scripting](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.Scripting/) (`CSharpScript`) |
|---|---|---|---|---|
| Language | C# expressions (Roslyn) | C#-like (own parser) | Dynamic LINQ dialect | Full C# (official) |
| Output | Delegate / Expression | Delegate / Expression | Expression tree | Compiled assembly |
| Startup / memory | Low | Low | Low | High |
| Dependency | Roslyn | None | None | Roslyn |

### Comparison

Because TagBites parses with Roslyn, it accepts modern C# syntax that neither DynamicExpresso's nor System.Linq.Dynamic.Core's own parsers do.  
Verified against DynamicExpresso 2.19.3 and System.Linq.Dynamic.Core 1.7.3 (see [LibraryFeatureComparer.cs](https://github.com/TagBites/TagBites.Expressions/blob/master/tests/TagBites.Expressions.Benchmarks/LibraryFeatureComparer.cs)):

| C# syntax | [TagBites](https://github.com/TagBites/TagBites.Expressions) | [DynamicExpresso](https://github.com/dynamicexpresso/DynamicExpresso) | [System.Linq.Dynamic.Core](https://github.com/zzzprojects/System.Linq.Dynamic.Core) |
|---|:---:|:---:|:---:|
| String interpolation `$"{x,6:0.00}"` (alignment + format) | ✓ | ✗ | ✗ |
| Switch expressions | ✓ | ✗ | ✗ |
| Pattern matching: relational, `and`/`or`/`not`, property | ✓ | ✗ | ✗ |
| Tuple/recursive deconstruction patterns (`x is (int a, int b)`) | ✓ | ✗ | ✗ |
| List patterns (`arr is [1, 2, 3]`) | ✓ | ✗ | ✗ |
| Tuples and tuple equality | ✓ | ✗ | ✗ |
| Array creation: sized and multidimensional | ✓ | ✗ | ✗ |
| Target-typed `new()` | ✓ | ✗ | ✗ |
| Anonymous objects (`new { X = 1 }`) | ✓ | ✗ | ✗ |
| `checked` / `unchecked` | ✓ | ✗ | ✗ |
| `nameof`, `sizeof` | ✓ | ✗ | ✗ |
| Null-forgiving `x!` | ✓ | ✗ | ✗ |
| Verbatim strings `@"..."` | ✓ | ✗ | ✗ |
| Digit separators `1_000` | ✓ | ✗ | ✗ |
| Generic method call with explicit type argument (`xs.OfType<int>()`) | ✓ | ✗ | ✗ |
| `is` / `as` | ✓ | ✓ | ✗ |
| `typeof`, `default(T)` | ✓ | ✓ | ✗ |
| Object and collection initializers | ✓ | ✓ | ✗ |
| Null-coalescing `??` / null-conditional `?.` | ✓ | ✓ | ✗ |
| Arithmetic and logical operators | ✓ | ✓ | ✓ |
| Member access and method calls | ✓ | ✓ | ✓ |
| Lambdas and LINQ | ✓ | ✓ | ✓ |
| Ternary | ✓ | ✓ | ✓ |

#### Benchmark

Parsing expressions: `"Math.Pow(x, y) + 5"` and `list.Where(x => x > limit).Select(x => Math.Pow(x, y)).Sum()`.  
Compared: TagBites.Expressions (v. 1.2.1) vs DynamicExpresso (v. 2.19.3) vs System.Linq.Dynamic.Core (v. 1.7.3).

| TestCase | TagBites.Expressions | DynamicExpresso | System.Linq.Dynamic.Core |
|---|---:|---:|---:|
| Parse | **`6,90 us`** (1,00x)<br>**6,02 KB** (1,00x) | `26,30 us` (3,81x)<br>30,75 KB (5,10x) | `3530,30 us` (512,0x)<br>276,79 KB (45,95x) |
| Parse_SharedEnv | **`4,72 us`** (1,00x)<br>**3,18 KB** (1,00x) | `14,02 us` (2,97x)<br>12,32 KB (3,88x) | `74,91 us` (15,85x)<br>101,08 KB (31,79x) |
| ParseLambda | **`84,85 us`** (1,00x)<br>**36,39 KB** (1,00x) | `244,32 us` (2,88x)<br>122,14 KB (3,36x) | `3661,42 us` (43,15x)<br>206,77 KB (5,68x) |
| ParseLambda_SharedEnv | **`29,69 us`** (1,00x)<br>**12,36 KB** (1,00x) | `220,56 us` (7,43x)<br>103,79 KB (8,40x) | `38,57 us` (1,30x)<br>35,26 KB (2,85x) |

> SharedEnv = shared options/interptreter/config.  
> SharedOptions for TagBites.Expressions uses `UseMemberCache = true`.

Benchmark source: [ParseToExpression.cs](https://github.com/TagBites/TagBites.Expressions/blob/master/tests/TagBites.Expressions.Benchmarks/ParseToExpression.cs).

## Links

- [Changelog](https://github.com/TagBites/TagBites.Expressions/blob/master/CHANGELOG.md)
- [Security policy](https://github.com/TagBites/TagBites.Expressions/blob/master/SECURITY.md)
- [License (MIT)](https://github.com/TagBites/TagBites.Expressions/blob/master/LICENSE)
