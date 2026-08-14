# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.5.0] - 2026-08-15

### Added
- Namespace-qualified type names on the right of `is` and `as` (`x is System.Int32`, `x as System.Text.StringBuilder`).
- Verbatim identifiers (`@value`).
- Unqualified `Equals` and `ReferenceEquals`.
- A standard numeric conversion chained with a user-defined one (`(Money)1` goes through `decimal`).
- A built-in operator after a user-defined conversion (`p + 1` where `Percentage` converts to `decimal`).
- The members of `object` on an interface type (`shape.ToString()`).
- Reference conversions between an interface and a non-sealed class (`shape as Node`).
- `+` on a nullable enum (`(DayOfWeek?)d + 1`).
- Subtraction with the enum on the right side (`1 - TypeCode.Boolean`).
- An empty property pattern on a non-nullable value type, which always matches (`5 is { }`).
- Conversion of the assigned value in an object initializer (`new Circle { R = 1 }`).

### Fixed
- A type name in a pattern is a type pattern instead of a constant comparison (`x is not Circle`), and it narrows the right side of `and`.
- An anonymous object member is no longer hidden by a member of its carrier; `new { Count = 7 }.Count` returned `1`.
- `double.NaN is double.NaN` is `true`; a pattern constant matches like `Equals`, not like `==`.
- An enum constant matches a boxed input (`((object)DayOfWeek.Monday) is DayOfWeek.Monday` returned `false`).
- Subtracting a number from a nullable enum keeps the enum type; it returned the underlying type.
- `-2147483648` is `int`; it was `long`.
- A relational operator and a relational pattern accept the literal zero on an enum (`day > 0`, `day is > 0`).
- A null literal compared with an enum lifts the operation instead of being rejected (`day == null` is `false`).
- A pattern that can never or always match is rejected (`5 is not { }`).
- An unreachable switch arm is rejected (`1 switch { { } => 1, _ => 2 }`).
- A pattern value that is not a constant is rejected (`ts is TimeSpan.Zero`).
- A constant that overflows outside `unchecked` is rejected (`int.MaxValue + 1`).
- `is null`, `?.` and `??` on a non-nullable value type are rejected (`5 is null`).
- A null literal against a non-nullable value type in `?:` and `switch` is rejected (`x > 0 ? 1 : null`).
- A null literal on both sides of `?:` and `??` is rejected (`null ?? null`).
- A constant negative array size is rejected (`new int[-1]`).

### Performance
- Conversion operator lookups are cached when options are shared with `UseMemberCache = true`, so a call-heavy expression parses up to four times faster.
- Method signatures are cached the same way, which helps repeated calls to overloaded methods.

## [1.4.0] - 2026-07-29

### Added
- `ExpressionParserOptions.Fork(resultType, resultCastType, parameters, useFirstParameterAsThis)`: creates a read-only variant that reuses the prepared, shared settings of the source instance (global members, included types, static imports, the reflection member cache and the resolution flags) while overriding the result type, result cast type, parameters or `this` handling. The shared lookups are prepared once and reused by every fork; only the parameters and `this` handling rebuild the parameter-specific part of the context. Pass `null` for any argument to inherit the source value.
- Jagged array creation (`new int[][] { ... }`, `new int[2][]`, and deeper forms such as `int[][][]`): previously rejected with an error. Every rank specifier after the first becomes part of the element type, so `int[][]` creates an array whose elements are `int[]`. Only the outermost dimension may carry a size, so `new int[3][1]` is still rejected. Example: `new int[][] { new[] { 1 }, new[] { 2, 3 } }[1][1]` returns `3`.
- Implicitly typed multidimensional arrays (`new[,] { { 1, 2 }, { 3, 4 } }`, `new[,,] { ... }`): only the explicitly typed form (`new int[,] { ... }`) worked before. The element type is inferred from the leaf elements, which must all have the same type. Example: `new[,] { { 1, 2 }, { 3, 4 } }[1, 0]` returns `3`.
- Index initializers in object initializers (`new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }`): each entry assigns through the indexer setter. Works with any type that has an accessible indexer setter, and can be mixed with member assignments. The collection form (`{ { "a", 1 } }`) still works. Example: `new Dictionary<string, int> { ["a"] = 1 }["a"]` returns `1`.
- Unbound generic types in `typeof` (`typeof(List<>)`, `typeof(Dictionary<,>)`).
- Tuples with more than seven elements (`(1, 2, 3, 4, 5, 6, 7, 8, 9)`): built with a nested `Rest` and accessible by `.ItemN` or element name.
- Target-typed `new()` as a method, constructor or indexer argument (`obj.Method(new())`, `obj.Method(new() { X = 1 })`): the type comes from the resolved parameter.
- `throw` expressions in `?:`, `??`, switch expression arms and as a lambda body (`x > 0 ? x : throw new ArgumentException()`, `items.Sum(x => throw ...)`), opt-in via the new `AllowThrowExpressions` option (default `false`).
- Constant and relational patterns against an `object` input, testing the runtime type first (`(object)5 is > 3` is `true`, `(object)5L is 5` is `false`), and the `and` pattern narrowing the type for its right side (`obj is int and > 5`).
- Enum constant patterns (`x is DayOfWeek.Monday`), relational patterns on enums via the underlying type (`x is >= DayOfWeek.Monday`), `var` deconstruction patterns (`(1, 2) is var (a, b)`), extended property patterns (`x is { B.Length: 1 }`) with null checks on intermediate members, and generic or array types on the right side of `is`/`as` (`x is List<int>`, `x as int[]`).
- Static member access on a generic type name (`Comparer<int>.Default`, `EqualityComparer<string>.Default`).
- Type name resolution for nested types (`typeof(List<int>.Enumerator)`), CLR names of the built-in types (`Int32`, `Int64`, `String`, ...), and namespace-qualified closed generics (`new System.Collections.Generic.List<int>()`).
- Lambdas bind to non-`Func`/`Action` delegate parameters (`Predicate<T>`, `Converter<T, R>`, `Comparison<T>`; for example `list.Find(x => x > 1)`), and a lambda body implicitly converts to the delegate's return type (`"ab".Sum(c => c)`).

### Fixed
- Numeric promotion and constant conversions follow the C# rules: `byte`/`ushort`/`char` convert directly to `uint`/`ulong` (`(byte)3 + 5ul` is `ulong`, `5u & (byte)3` is `uint`), an in-range `int` constant converts to the smaller or unsigned operand type (`5ul + 5` is `10ul`, `3u - 1` is `2u`, `new byte[] { 1, 2 }`), a non-negative `long` constant converts to `ulong` (`5L + 5ul` is `10ul`), `uint` with a signed operand promotes to `long` (also lifted: `5u + (int?)4` is `long?`), and unary minus on `uint` gives `long` (`-(5u)` is `-5L`).
- The conditional operator and switch expression arms require an implicit conversion to one of the operand types, and `??` follows the C# order (the right operand converts to the unwrapped type, else to the nullable type itself, else the left converts to the right type). Mixed cases such as `true ? (int?)4 : 5L` or `(int?)4 ?? (uint?)3u` are rejected instead of promoting to `long`; binary operators still promote (`(int?)4 + 5L` is `long?`). A nullable numeric never converts implicitly to a non-nullable type.
- Expressions the C# compiler rejects are now rejected: `==`/`!=` between `object` and a value type (`(object)5 == 5` was a silent reference comparison returning `false`), `&&`/`||` on `bool?` operands (`&` and `|` keep the three-valued logic), `!` on non-bool operands (`!5` was a bitwise complement), and casts between `bool` and numeric, `char` or enum types (`(int)true` returned `1`).
- A pattern constant must convert to the input type like in C#: `5L is < 3.5`, `5 is > 3f` and `'B' is 5` are rejected, while `2.5m is > 3` and `66 is 'B'` work through constant conversion.
- Switch-arm pattern variables are scoped to their own arm, so the same name can appear in several arms.
- The `as` operator with a nullable type whose underlying type does not match a value-type operand gives `null` like C# (`200 as long? ?? -1` is `-1`); previously it failed with an internal error.
- Casts between `decimal` and an enum type work in both directions (`(DayOfWeek)2.5m`, `(decimal)DayOfWeek.Friday`) by going through the enum's underlying type.
- The `~` operator works on enum types (`flags & ~StringSplitOptions.TrimEntries`): the complement is computed on the underlying type and converted back.
- Members and method calls on a `typeof(...)` value resolve against `System.Type` (`typeof(int).Name`; `typeof(int[]).GetElementType()` with `AllowReflection`); the value was treated as a static type reference (the mechanism behind `int.MaxValue`).
- A generic type added to `IncludedTypes` resolves by name; a closed generic (`typeof(SortedSet<int>)`) is available only with those exact arguments, an open definition (`typeof(Queue<>)`) with any.
- Overload resolution betterness matches the C# compiler: an expanded `params` candidate compares by element type (`"a,b;c".Split(',', ';')` gives 3 parts, not 2), a more specific parameter type beats an assignable one (`1L.Equals(1)` is `true`), a closed generic `IEnumerable<T>` overload beats `params object[]` (`string.Join(",", new[] { 3, 1, 2 })` is `"3,1,2"`), and a not-yet-bound lambda argument no longer breaks ranking (`items.Max(x => x.Value)` threw an internal error), with the more specific signature as a tie-break.
- Generic arguments infer from array parameters (`Array.Exists(new[] { 1, 2 }, x => x > 1)`, `Array.ConvertAll`), through the interfaces of a non-generic type (`"racecar".Count()` - `string` is `IEnumerable<char>`), from types nested in a lambda parameter type (`GroupJoin`), and `Aggregate` with a result selector no longer throws during inference.
- Named tuple element names flow everywhere C# preserves them: from an array literal into LINQ lambdas (`new[] { (Name: "x", Val: 1) }.Sum(t => t.Val)`), through nested `.ItemN` access, through a conditional where both branches agree, and from method return metadata (`items.Index().First().Index`, `a.Zip(b).First().Second`).
- Tuples with different element types compare element-wise with implicit conversions (`(1, 2) == (1L, 2L)`).
- An implicit array infers the C# best common element type (`new[] { 1, 2L }` is `long[]`, `new[] { (byte)1, 2 }` is `int[]`) and accepts `null` literal elements (`new[] { "a", null }` is `string[]`).
- `Cast<T>()` and `OfType<T>()` resolve on collections that implement only the non-generic `IEnumerable`, such as multidimensional arrays.
- `Enumerable.Max`/`Min` over an enum array returns the enum type instead of its underlying integer.
- Comparisons with a nullable enum operand are lifted (`(DayOfWeek?)x == DayOfWeek.Monday`).
- A numeric operand is promoted to a user-defined operator's parameter type (`TimeSpan.FromHours(1) * 2`).
- The bare `null` literal binds to reference and nullable parameters (`string.IsNullOrEmpty(null)`).
- An anonymous object's `ToString()` returns the C# anonymous type format (`{ A = 1, B = x }`) instead of the internal type name.
- The string literal `"default"` is no longer parsed as the `default` keyword (`"default".Length` works).
- Escape sequences resolve in interpolated string text and format clauses (`$"{ts:hh\\:mm}"`, `$"a\tb{x}"`).

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

[1.4.0]: https://github.com/TagBites/TagBites.Expressions/compare/1.3.2...1.4.0
[1.3.2]: https://github.com/TagBites/TagBites.Expressions/compare/1.3.1...1.3.2
[1.3.1]: https://github.com/TagBites/TagBites.Expressions/compare/1.3.0...1.3.1
[1.3.0]: https://github.com/TagBites/TagBites.Expressions/compare/1.2.1...1.3.0
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
