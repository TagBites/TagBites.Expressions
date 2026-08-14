# Configuration

`ExpressionParserOptions`:

| Option | Purpose |
|---|---|
| `AllowReflection` | Allow reflection APIs. (default: `false`) |
| `AllowThrowExpressions` | Allow `throw` expressions in `?:` and `??`. (default: `false`) |
| `Parameters` | Typed parameters of the resulting lambda. |
| `UseFirstParameterAsThis` | Use the first parameter as `this` so its members need no prefix. |
| `GlobalMembers` | Named values and delegates usable by name; a member named `this` is implicit. |
| `IncludedTypes` | Types (and static classes) an expression may reference by name. |
| `IgnoreBuiltInTypes` | Disable the fixed set of common framework types otherwise available by short name, independent of `IncludedTypes`. (default: `false`) |
| `TypeResolver` | Fallback `Func<string, Type?>` that resolves a type from its name (optionally namespace-qualified) when it is not found elsewhere. |
| `StaticImports` | Imported static classes, as if `using static` was applied (e.g. `Sqrt(x)`, `PI`). Parameters and global members take precedence. |
| `CustomPropertyResolver` | Resolve members at runtime, e.g. against types defined only at runtime. |
| `ResultType` | Require the result to be this type. An implicit conversion is applied if needed, otherwise parsing fails. |
| `ResultCastType` | Force the result to this type with an explicit cast, e.g. to compile every expression as `Func<object>`. |
| `UseMemberCache` | Cache reflected members (methods, indexers, extension methods) on this options instance; enable when reusing the same options across many parses. (default: `false`) |

**CustomPropertyResolver**:  
It is only called for `instance.Member`, it needs an instance to work on. That can be an ordinary parameter, accessed explicitly - `p.Age` works for any parameter name. A bare name like `Age` also works, but it is then resolved implicitly as `this.Age`, so a `this` must be set up first: `UseFirstParameterAsThis`, or a `this` entry in `GlobalMembers`.

**Result type:**  
`ResultType` is a contract: the expression must produce this type. A C# implicit conversion (like `int` -> `long`) is applied automatically; anything else is a parse error. Use it to require, for example, that a filter is a `bool`.  
`ResultCastType` forces the return type with an explicit cast, so unrelated expressions can share one delegate signature. It also allows casts that are not implicit, such as `double` -> `int`.

The two combine: to run many rules through a single `Func<object>` while still requiring each to be boolean, set `ResultType = typeof(bool)` (reject anything non-boolean) together with `ResultCastType = typeof(object)`.

**Type resolution:**  
When an expression references a type by name (e.g. `DateTime.Now`, `new List<int>()`, `(TimeSpan)x`), the parser resolves it in this order: `ResultType`, `Parameters`, `IncludedTypes`, the built-in types, then `TypeResolver`.

By default a fixed set of common framework types is always available by short name, regardless of `IncludedTypes`:  
- Time: `TimeSpan`, `DateTime`, `DateTimeOffset`, `DateTimeKind`, `DayOfWeek`
- Text: `StringComparison`, `StringSplitOptions`
- Math: `Math`, `MidpointRounding`
- Common: `Guid`, `KeyValuePair<,>`
- Collections: `Enumerable`, `List<>`, `Dictionary<,>`, `HashSet<>`, `IList<>`, `IEnumerable<>`, `ICollection<>`, `IReadOnlyList<>`, `IReadOnlyCollection<>`, `IDictionary<,>`, `IReadOnlyDictionary<,>`, `ISet<>`
- Other: `Convert`, `CultureInfo`

Set `IgnoreBuiltInTypes = true` to make the parser accept only the types you explicitly allow. The C# primitive keywords (`int`, `string`, `bool`, `object`, ...) are **language keywords and are always available**, independent of this option.

`TypeResolver` is a `Func<string, Type?>` fallback consulted last. It receives the type name, which may be namespace-qualified (for example `System.Text.StringBuilder`) or a short name, and returns the matching `Type` or `null` if it does not recognize the name. A generic type name is suffixed with an apostrophe and its type-argument count (for example `List'1`, `Dictionary'2`), and the resolver must return the open generic definition (`typeof(List<>)`); the parser closes it with the supplied type arguments.

```csharp
var options = new ExpressionParserOptions
{
    TypeResolver = name => name switch
    {
        "StringBuilder" or "System.Text.StringBuilder" => typeof(StringBuilder),
        "ImmutableArray'1" => typeof(ImmutableArray<>),
        _ => null
    }
};
```

**Reuse and immutability**:  
Like `JsonSerializerOptions`, an `ExpressionParserOptions` instance becomes read-only after it is first used for parsing, enabling fast concurrent use.

**Fork**:  
`Fork` reuses the prepared, shared settings of an instance - global members, included types, static imports, the reflection member cache and the resolution flags - and overrides only the result type, parameters or `this` handling. Use it when one set of options is shared, but different delegates must be produced from it, so the shared lookups are prepared once instead of per delegate.

```csharp
var options = new ExpressionParserOptions { Parameters = { (typeof(int), "n") } };

var asLong = ExpressionParser.Compile<Func<int, long>>("n * 2", options.Fork(typeof(long)));
var asObject = ExpressionParser.Compile<Func<int, object>>("n * 2", options.Fork(typeof(object)));

asLong(10);    // 20L
asObject(10);  // 20 (boxed int)
```

## Non-standard options
These opt-in options (all default to `false`) make the parser accept syntax or semantics that real C# does not:

| Option | Purpose |
|---|---|
| `AllowStringRelationalOperators` | Allow `<` / `<=` / `>` / `>=` on strings, compared ordinally via `string.Compare` - not valid in real C#. |
| `AllowRuntimeCast` | Allow custom keywords `typeis` / `typeas` / `typecast` against runtime type names. |
| `IgnoreCase` | Resolve parameters, variables, global members, type members and `IncludedTypes` case-insensitively. For `GlobalMembers`/`IncludedTypes`, case-insensitive name collisions are checked before parsing. |
