# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-07-13

### Added
- Anonymous objects (`new { X = 1, Y = 2 }`), internally mapped to `DynamicObject` with parse-time member validation and value equality (`Equals`/`GetHashCode`), without generating a new type.
- Recursive/tuple deconstruction patterns (`x is (int a, int b)`, `x is Point(int x, int y)`), including `Deconstruct` methods.
- List patterns (`arr is [1, 2, 3]`, `arr is [1, .., 3]`).
- Target-typed `new()`, including inside object/collection initializers (`new List<Point> { new() { X = 1 } }`).
- Collection initializers for `Add`-based collections (`new List<int> { 1, 2, 3 }`, `new Dictionary<string, int> { { "a", 1 } }`), not just arrays.
- `AllowStringRelationalOperators` option to opt into `<` / `<=` / `>` / `>=` on strings (ordinal, via `string.Compare`) - disabled by default, matching real C#.

### Fixed
- `<` / `<=` / `>` / `>=` on strings are rejected by default, matching real C# (previously always allowed via `string.Compare`).
- A discard (`_`) used as a nested sub-pattern (for example `(1, 2) is (1, _)`) returned the matched value instead of `true`.
- Reflection-based member lookups are now trim/AOT-compatible (annotated for the trimmer, so publishing with trimming enabled no longer strips members the parser depends on).

### Known limitations
- Target-typed `new()` is not yet inferred as a method call argument (`obj.Method(new())`); use an explicit type there for now.

## [1.1.2] - 2026-07-08

### Added
- Index-from-end operator in indexers: `x[^1]` for arrays, strings and `IList`/`IReadOnlyList` (lowered to `x[length - n]`, so no dependency on `System.Index`).

## [1.1.1] - 2026-07-08

### Fixed
- Alignment in interpolated strings (`$"{x,6}"`) was ignored; it is now honored together with format specifiers.

## [1.1.0] - 2026-07-08

A large expansion of the supported C# expression grammar, plus several correctness and performance improvements.

### Added
- `switch` expressions with full pattern support: type, constant, relational, `and`/`or`/`not`, property, positional and `var` patterns, `when` guards, declaration patterns and exhaustive (no-discard) switches.
- Tuple equality (`==` / `!=`, compared element-wise).
- Array creation with explicit sizes and multidimensional arrays (`new int[2, 3]`, `new int[,] { { 1, 2 }, { 3, 4 } }`).
- `typeof`, `default(T)`, `nameof`, `sizeof`, `checked` and `unchecked`.
- Null-forgiving operator (`x!`) and bitwise complement (`~`).
- `params` method arguments.
- Custom-named indexers (for example indexing a `string`).
- Enum arithmetic (`E + U`, `E - E`, bitwise and comparison operators, following the C# rules).
- User-defined `implicit` conversion operators and nullable conversions.
- More built-in types usable by name (`Dictionary<,>`, `HashSet<>`, `IReadOnlyList<>`, `Guid`, `Convert`, and others).
- `ResultType` now applies an implicit conversion when one exists (for example `int` -> `long`).

### Fixed
- Small integers (`byte`, `sbyte`, `short`, `ushort`, `char`) are now promoted to `int` for arithmetic, bitwise and unary operators, matching C#.
- Shift operators no longer coerce both operands to a common type (`1L << 40`).
- Error when resolving array types (`typeof(int[])`, `default(int[])`, casts).

### Performance
- Parse only the expression via `SyntaxFactory.ParseExpression` instead of a full script compilation unit - several times faster with fewer allocations.
- Reflection detection is folded into the build pass, removing a separate expression-tree walk.

## [1.0.8] - 2025-04-23

### Fixed
- Duplicate methods coming from `Enumerable` when resolving extension methods.

## [1.0.7] - 2025-03-23

### Added
- Extension methods from types listed in `IncludedTypes`.

## [1.0.6] - 2025-03-05

### Added
- `ExpressionParser.Compile` and `Invoke` helpers.
- `GlobalMembers` - named values and delegates usable by name in an expression.

### Changed
- Swapped the argument order of the `typeis` / `typeas` / `typecast` runtime-cast keywords.

## [1.0.5] - 2025-03-04

### Added
- Void expressions with conditional (`?.`) calls.

## [1.0.4] - 2025-03-04

### Fixed
- A void method could be invoked in a value context.

## [1.0.3] - 2025-03-03

### Fixed
- A delegate passed as a parameter could not be invoked directly.
- Corrections to member full path preservation.

## [1.0.2] - 2024-10-24

### Added
- The full member path is preserved and exposed to `CustomPropertyResolver`.

## [1.0.1] - 2024-01-18

### Added
- `netstandard2.0` target.

## [1.0.0] - 2024-01-18

### Added
- Initial release. Converts C# text expressions into `System.Linq.Expressions` using Roslyn.

[1.2.0]: https://github.com/TagBites/TagBites.Expressions/compare/1.1.2...1.2.0
[1.1.2]: https://github.com/TagBites/TagBites.Expressions/compare/1.1.1...1.1.2
[1.1.1]: https://github.com/TagBites/TagBites.Expressions/compare/1.1.0...1.1.1
[1.1.0]: https://github.com/TagBites/TagBites.Expressions/compare/1.0.8...1.1.0
[1.0.8]: https://github.com/TagBites/TagBites.Expressions/compare/1.0.7...1.0.8
[1.0.7]: https://github.com/TagBites/TagBites.Expressions/compare/1.0.6...1.0.7
[1.0.6]: https://github.com/TagBites/TagBites.Expressions/compare/1.0.5...1.0.6
[1.0.5]: https://github.com/TagBites/TagBites.Expressions/compare/1.0.4...1.0.5
[1.0.4]: https://github.com/TagBites/TagBites.Expressions/compare/1.0.3...1.0.4
[1.0.3]: https://github.com/TagBites/TagBites.Expressions/compare/1.0.2...1.0.3
[1.0.2]: https://github.com/TagBites/TagBites.Expressions/compare/1.0.1...1.0.2
[1.0.1]: https://github.com/TagBites/TagBites.Expressions/compare/1.0.0...1.0.1
[1.0.0]: https://github.com/TagBites/TagBites.Expressions/releases/tag/1.0.0
