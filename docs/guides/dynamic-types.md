# Dynamic / Runtime-defined types

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
