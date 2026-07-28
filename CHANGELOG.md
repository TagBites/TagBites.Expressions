# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `ExpressionParserOptions.Fork(resultType, resultCastType, parameters, useFirstParameterAsThis)`: creates a read-only variant that reuses the prepared, shared settings of the source instance (global members, included types, static imports, the reflection member cache and the resolution flags) while overriding the result type, result cast type, parameters or `this` handling. The shared lookups are prepared once and reused by every fork; only the parameters and `this` handling rebuild the parameter-specific part of the context. Pass `null` for any argument to inherit the source value.
- Jagged array creation (`new int[][] { ... }`, `new int[2][]`, and deeper forms such as `int[][][]`): previously rejected with an error. Every rank specifier after the first becomes part of the element type, so `int[][]` creates an array whose elements are `int[]`. Only the outermost dimension may carry a size, so `new int[3][1]` is still rejected. Example: `new int[][] { new[] { 1 }, new[] { 2, 3 } }[1][1]` returns `3`.
- Implicitly typed multidimensional arrays (`new[,] { { 1, 2 }, { 3, 4 } }`, `new[,,] { ... }`): only the explicitly typed form (`new int[,] { ... }`) worked before. The element type is inferred from the leaf elements, which must all have the same type. Example: `new[,] { { 1, 2 }, { 3, 4 } }[1, 0]` returns `3`.
- Index initializers in object initializers (`new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }`): each entry assigns through the indexer setter. Works with any type that has an accessible indexer setter, and can be mixed with member assignments. The collection form (`{ { "a", 1 } }`) still works. Example: `new Dictionary<string, int> { ["a"] = 1 }["a"]` returns `1`.
- Unbound generic types in `typeof` (`typeof(List<>)`, `typeof(Dictionary<,>)`).
- Tuples with more than seven elements (`(1, 2, 3, 4, 5, 6, 7, 8, 9)`): built with a nested `Rest` and accessible by `.ItemN` or element name.
- Target-typed `new()` as a method, constructor or indexer argument (`obj.Method(new())`, `obj.Method(new() { X = 1 })`): the type comes from the resolved parameter.
- `throw` expressions in `?:` and `??` (`x > 0 ? x : throw new ArgumentException()`), opt-in via the new `AllowThrowExpressions` option (default `false`).

### Fixed
- Implicit arrays now infer the C# best common element type instead of requiring identical element types. `new[] { 1, 2L }` is `long[]` and `new[] { (byte)1, 2 }` is `int[]`; previously both failed with a type-not-found error.
- `Enumerable.Max` and `Enumerable.Min` with a selector lambda (for example `items.Max(x => x.Value)`) threw an internal `Extension node must override the property Expression.Type` error during overload resolution. The not-yet-bound lambda argument is now skipped when ranking overloads, and a more-specific-signature tie-break selects the correct member (a concrete `Func<TSource, int>` result over the fully generic `Func<TSource, TResult>`). `Sum` and `Average` were not affected.
- Overload resolution now prefers a generic `IEnumerable<T>` overload over a `params object[]` one. `string.Join(",", new[] { 3, 1, 2 })` bound the array to `params object[]` and returned `"System.Int32[]"`; it now binds the generic `Join<T>(string, IEnumerable<T>)` overload and returns `"3,1,2"`. The betterness comparison previously read the open generic parameters (`IEnumerable<T>`) instead of the closed ones (`IEnumerable<int>`).
- LINQ and other `IEnumerable<T>` extension methods now resolve on `string`, which is `IEnumerable<char>`. `"racecar".Count()`, `"abc".Reverse()` and similar calls previously failed with a method-not-found error, because generic-argument inference did not look through the interfaces of a non-generic type.
- Members of a `typeof(...)` value now resolve against `System.Type`. `typeof(int).Name` previously failed with `Unknown member Name` because the value was treated as a static type reference (the mechanism behind `int.MaxValue`). `Name` and `IsValueType` work without reflection; other `Type` members require `AllowReflection`.
- `Enumerable.Aggregate` with a result selector (the three-argument overload) no longer throws an index-out-of-range error during generic inference.
- `Enumerable.GroupJoin` and similar calls now infer generic arguments nested in a lambda parameter type (for example `Func<T, IEnumerable<TInner>, TResult>`).
- Switch-arm pattern variables are scoped to their own arm, so the same name can appear in several arms.
- Named-tuple element names flow from an array literal into LINQ lambdas (for example `new[] { (Name: "x", Val: 1) }.Sum(t => t.Val)`).
- A numeric operand is promoted to a user-defined operator's parameter type (for example `TimeSpan.FromHours(1) * 2`).
- The bare `null` literal binds to reference and nullable parameters (for example `string.IsNullOrEmpty(null)`).
- Enum constant patterns (`x is DayOfWeek.Monday`) and `var` deconstruction patterns (`(1, 2) is var (a, b)`) in `is` expressions.
- Namespace-qualified closed generic types (for example `new System.Collections.Generic.List<int>()`).
- Nested tuple element names now flow through direct `.ItemN` access (for example `(Name: "x", (Inner: 5, 6)).Item2.Inner`).
- `Enumerable.Max`/`Min` over an enum array now returns the enum type instead of its underlying integer.
- `uint` combined with a signed `sbyte`/`short`/`int` promotes both operands to `long` (for example `1u + 1` yields `2L`).
- Relational patterns compare enum operands via the underlying type (for example `x is >= DayOfWeek.Monday`).
- The string literal `"default"` is no longer parsed as the `default` keyword (`"default".Length` and `x ?? "default"` now work).
- Lambdas bind to non-`Func`/`Action` delegate parameters such as `Predicate<T>`, `Converter<T, R>` and `Comparison<T>` (for example `list.Find(x => x > 1)`, `list.ConvertAll(x => x * 2)`).
- Generic and array types resolve on the right side of `is`/`as` (for example `x is List<int>`, `x as int[]`).
- Comparisons with a nullable enum operand are lifted (for example `(DayOfWeek?)x == DayOfWeek.Monday`).
- C# constant conversions apply to `int` literals in range of a smaller integral type (for example `new byte[] { 1, 2 }`, `new List<byte> { 1 }`, `new sbyte[] { -5 }`).
- Tuple element names survive a conditional where both branches agree (for example `(c ? (a: 1, b: 2) : (a: 3, b: 4)).a`).
- Tuples with different element types compare element-wise with implicit conversions (for example `(1, 2) == (1L, 2L)`).
- A lambda body implicitly converts to the delegate's return type (for example `"ab".Sum(c => c)`, `bytes.Sum(b => b)`).
- Tuple element names declared by method return metadata are visible (for example `items.Index().First().Index`, `a.Zip(b).First().Second`).
- Escape sequences resolve in interpolated string text and format clauses (for example `$"{ts:hh\\:mm}"`, `$"a\tb{x}"`).
- Extended property patterns (`x is { B.Length: 1 }`), with null checks on the intermediate members like C#.
- An implicit array infers its element type when some elements are `null` literals (for example `new[] { "a", null }` is `string[]`).

### Performance
- Indexer property lookups are cached in the shared member cache.
- Instance member, user-defined conversion operator and `ValueTuple.Create` lookups are cached; constructed generic methods are cached per options. All caches live on the options instance, so collectible assemblies can unload.
- A lambda body is resolved once and reused across overload candidates with the same parameter types (for example the `Enumerable.Max`/`Min`/`Sum` overload sets).

## [1.3.2] - 2026-07-24

### Fixed
- Namespace-qualified types now resolve inside expression bodies, not only in `new`/cast/`typeof` positions. Static access such as `System.Math.Pow(2, 3)`, `System.Math.PI` and `System.TimeSpan.FromMinutes(2)` previously failed with an unknown-identifier error. The `TypeResolver` fallback is consulted with the full dotted name (for example `System.Math`) for these accesses, and a qualifier that does not match a resolved type's actual namespace (for example `System.Text.Math`) is rejected.

## [1.3.1] - 2026-07-24

### Added
- `IgnoreBuiltInTypes` option (default `false`): hides the fixed set of common framework types otherwise available by short name regardless of `IncludedTypes` (`DateTime`, `TimeSpan`, `Guid`, `Math`, `Convert`, `Enumerable`, `List<>`, `Dictionary<,>`, the common collection interfaces, and others - see the README). Set it to `true` to accept only explicitly allowed types; the C# primitive keywords (`int`, `string`, `bool`, ...) remain always available.
- `TypeResolver` option (`Func<string, Type?>`): a fallback consulted after `ResultType`, `Parameters`, `IncludedTypes` and the built-in types, letting you resolve a type from its name. The name may be namespace-qualified (for example `System.Text.StringBuilder`) or a short name; generic type names carry an apostrophe + arity suffix (`List'1`, `Dictionary'2`) and expect the open generic definition. Return `null` when the name is not recognized.

### Performance
- Faster parsing: option state prepared once and reused across parses, cached reflection lookups, fewer hot-path allocations.
- Allocation-free candidate handling.
- Early lambda-arity rejection.
- Element-type-info propagation skipped when no `CustomPropertyResolver` is set.
- Lock-free member cache reads (`ConcurrentDictionary`).

## [1.3.0] - 2026-07-23

### Added
- Named tuple element names (`(Name: "Bob", Age: 30).Name`): supports names declared explicitly, names inferred from identifiers and member accesses (`(a, x.B)`), and names that flow through generic and LINQ chains (`people.Select(p => (Name: p.Name, Age: p.Age)).First().Name`), matching C# - including the same rules for reserved (`ItemN`, `Rest`, ...), duplicate and conflicting names.
- `StaticImports` option: a collection of static classes whose public static methods, fields, properties and constants can be used unqualified, as if `using static` was applied (for example, importing `Math` makes `Sqrt(x)`, `Max(a, b)`, `PI` and `E` available). Non-static classes are rejected, and instance members, global members and instance types always take precedence.
- `IgnoreCase` option: resolve parameters, variables, global members, type members and `IncludedTypes` case-insensitively.
- Full support for the bare `default` literal: it is now target-typed wherever C# can infer the type (method arguments, comparisons, `??`, ternary, casts), not only when `ResultType` is set. It still fails, like C#, where there is no target type (a bare `default`, `default == default`, an overloaded-method argument).
- Named arguments on method, constructors, indexers and extension-method calls (`obj.Sum(b: 2, a: 1)`) now can bind by name instead of being passed positionally. Supports reordering, mixing positional and named arguments, and skipping optional parameters (`obj.Concat3("x", c: "z")`), and participates in overload and generic-method resolution. Honors the `IgnoreCase` option. 

### Changed
- `ExpressionParserOptions` is now read-only after it is first used for parsing: property setters throw `InvalidOperationException` and the `Parameters`, `GlobalMembers`, `IncludedTypes` and `StaticImports` collections throw `NotSupportedException` on mutation.

### Fixed
- Null-conditional access (`?.`) evaluated its receiver twice (once for the null check, once for the access) instead of once; a receiver with a side effect, like a method call, was invoked twice.

### Deprecated
- `UseReducedExpressions` is obsolete, the parser always produces standard expression nodes, so there is nothing left to reduce.

## [1.2.1] - 2026-07-17

### Added
- Delegate-typed members can be invoked with method-call syntax (`DelegateField(args)`), including delegates returned from `CustomPropertyResolver`, matching C#.
- `UseMemberCache` option (default `false`): when enabled, reflected members (methods, indexers, extension methods) are memoized per `ExpressionParserOptions` instance. Turn it on when reusing the same options across many parses to skip repeated reflection.

### Fixed
- `CustomPropertyResolver`: element type info now propagates through method and indexer chains over dynamic collections, so chains like `people.Where(p => p.Age > 18).FirstOrDefault()?.Name` resolve members correctly (previously only a bare lambda parameter inherited it).

### Performance
- Overload resolution rejects candidates with unfilled required parameters before running generic inference and lambda binding - a large speedup and allocation drop for LINQ-heavy expressions with many overloads (for example `Sum`).
- Conversion-operator lookups are short-circuited for primitive types, so numeric conversions (`int` -> `double`) no longer scan reflection.
- Fewer allocations across the parse hot path: eliminated enumerator boxing and several intermediate lists/arrays, and index-based loops replace LINQ in hot spots.

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

[1.2.1]: https://github.com/TagBites/TagBites.Expressions/compare/1.2.0...1.2.1
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
