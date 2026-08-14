# FastExpressionCompiler

`ExpressionParser.Parse()` returns a plain `LambdaExpression`, so it can be compiled with any compiler instead of the built-in `Compile()`. [FastExpressionCompiler](https://github.com/dadhi/FastExpressionCompiler) is a drop-in, dependency-free replacement for `LambdaExpression.Compile()` that produces the same delegate much faster:

```
dotnet add package FastExpressionCompiler
```

```csharp
using FastExpressionCompiler;

var lambda = ExpressionParser.Parse("Math.Pow(x, y) + 5", options);
var func = (Func<double, double, double>)lambda.CompileFast();
```

## Benchmark

| Expression | `Compile()` | `CompileFast()` | Speedup |
|---|---:|---:|---:|
| `Math.Pow(x, y) + 5` | 28.23 µs | 2.24 µs | ~12.6x |
| `x switch { ... }` with LINQ `Select`/`Sum` | 180.92 µs | 5.98 µs | ~30x |


The more complex the expression tree, the bigger the gap, since most of the reflection-emit overhead `Compile()` pays per node is avoided by `CompileFast()`. 

Benchmark source: [CompileToDelegate.cs](https://github.com/TagBites/TagBites.Expressions/blob/master/tests/TagBites.Expressions.Benchmarks/CompileToDelegate.cs).
