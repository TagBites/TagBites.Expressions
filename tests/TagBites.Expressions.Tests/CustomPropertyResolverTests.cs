using System.Linq.Expressions;

namespace TagBites.Expressions.Tests;

/// <summary>
/// <remarks>
/// Example data: 
/// Alice(30) and Carol(42), Bob(15)
/// Alice.Children = [Bob, Carol]
/// </remarks>
/// </summary>
public class CustomPropertyResolverTests
{
    [Fact]
    public void MemberAsDelegate()
    {

        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(object), "this") },
            UseFirstParameterAsThis = true,
            CustomPropertyResolver = ctx =>
            {
                if (ctx.MemberName == "Doubler")
                {
                    static int Doubler(int x) => x * 2;
                    return Expression.Constant((Func<int, int>)Doubler);
                }

                return null;
            }
        };

        var result = ExpressionParser.Invoke<int>("Doubler(10)", options, (object?)null);

        Assert.Equal(20, result);
    }

    [Theory]
    [InlineData("One", 1)]
    [InlineData("One + Two", 3)]
    [InlineData("One + Two * Ten", 21)]
    public void MemberAsValue(string script, int expected)
    {
        var words = new Dictionary<string, int> { ["One"] = 1, ["Two"] = 2, ["Ten"] = 10 };

        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(object), "this") },
            UseFirstParameterAsThis = true,
            CustomPropertyResolver = ctx => words.TryGetValue(ctx.MemberName, out var n) ? Expression.Constant(n) : null
        };

        var result = ExpressionParser.Invoke<int>(script, options, (object?)null);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("A", "this.A")]
    [InlineData("A.B.C", "this.A.B.C")]
    [InlineData("p.A", "p.A")]
    [InlineData("p.A.B.C", "p.A.B.C")]
    public void MemberFullPath(string script, string expectedPath)
    {
        var maxPath = "";

        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(object), "this"), (typeof(object), "p") },
            UseFirstParameterAsThis = true,
            CustomPropertyResolver = ctx =>
            {
                if (ctx.MemberFullPath!.Length > maxPath.Length)
                    maxPath = ctx.MemberFullPath;

                return Expression.Constant(null, typeof(object));
            }
        };

        ExpressionParser.Parse(script, options);

        Assert.Equal(expectedPath, maxPath);
    }

    [Theory]
    [InlineData("Founder.Age", 30)]
    public void SingleMember(string script, object value)
    {
        var result = ExpressionParser.Invoke<object>(script, CreateOptions(), CreateSource());

        Assert.Equal(value, result);
    }

    [Theory]
    [InlineData("People.Count()", 3)]
    [InlineData("People.Sum(p => p.Age)", 30 + 42 + 15)]
    [InlineData("People.Sum(p => p.Children?.Count() ?? 0)", 2)]
    [InlineData("People.Sum(p => p.Children?.Select(x => x.Age).Sum())", 42 + 15)]
    [InlineData("People.Where(p => p.Age > 18).Count()", 2)]
    [InlineData("People.Where(p => p.Name == \"Bob\").Count()", 1)]
    [InlineData("People.Where(p => p.Name == \"Bob\").FirstOrDefault()?.Name", "Bob")]
    [InlineData("People.Where(p => p.Name == \"Bob\").FirstOrDefault()?.Age", 15)]
    [InlineData("People.Select(p => new { p.Name, p.Age }).FirstOrDefault(x => x.Name == \"Bob\")?.Age", 15)]
    [InlineData("People.SelectMany(p => p.Children?.Select(x => x.Age) ?? new List<int>()).Sum()", 42 + 15)]
    [InlineData("People.SelectMany(p => p.Children ?? new List<DynamicRecord>()).Select(x => x.Age).Sum()", 42 + 15)]
    [InlineData("string.Join(\", \", People.Select(p => p.Name).OrderBy(x => x))", "Alice, Bob, Carol")]
    [InlineData("People.GroupBy(p => p.Age > 18).Count()", 2)]
    [InlineData("People.GroupBy(p => p.Age > 18).First(g => !g.Key).Count()", 1)]
    public void Lambda(string script, object value)
    {
        var result = ExpressionParser.Invoke<object>(script, CreateOptions(), CreateSource());

        Assert.Equal(value, result);
    }

    [Theory]
    [InlineData("People.Where(p => p.Salary > 1000)")]
    [InlineData("People.Age")]
    [InlineData("People.FirstOrDefault().Unknown")]
    public void UnknownFieldError(string script)
    {
        var success = ExpressionParser.TryParse(script, CreateOptions(), out _, out var error);

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(true, 30)]  // Founder = Alice
    [InlineData(false, 42)] // Founder = Carol
    public void SharedSchemaAcrossDataSources(bool founderIsAlice, int expectedFounderAge)
    {
        var func = ExpressionParser.Compile<Func<DynamicRecord, int>>("Founder.Age", CreateOptions());
        var age = func(CreateSource(founderIsAlice));

        Assert.Equal(expectedFounderAge, age);
    }

    private static Expression? Resolver(DynamicRecordSchema rootSchema, IExpressionMemberResolverContext context)
    {
        if (context.Instance.Type != typeof(DynamicRecord))
            return null;

        // Member type
        var instanceSchema = context.InstanceTypeInfo as TypeSchema
            ?? (context.MemberFullPath == "this." + context.MemberName ? rootSchema.GetValueOrDefault("this") : null);
        if (instanceSchema == null)
            return null;

        if (instanceSchema.Fields == null && instanceSchema.Name != null)
        {
            instanceSchema = rootSchema.GetValueOrDefault(instanceSchema.Name);
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

        var method = typeof(DynamicRecord).GetMethod(nameof(DynamicRecord.GetFieldValue))!.MakeGenericMethod(fieldType);
        var result = Expression.Call(context.Instance, method, Expression.Constant(context.MemberName));

        return !isKnownType
            ? context.IncludeTypeInfo(result, fieldTypeScheme)
            : result;
    }

    private static ExpressionParserOptions CreateOptions()
    {
        return new ExpressionParserOptions
        {
            Parameters = { (typeof(DynamicRecord), "this") },
            UseFirstParameterAsThis = true,
            CustomPropertyResolver = x => Resolver(CreateSourceSchema(), x)
        };
    }
    private static DynamicRecordSchema CreateSourceSchema()
    {
        var personSchema = new TypeSchema(new Dictionary<string, TypeSchema>
        {
            ["Name"] = new(typeof(string)),
            ["Age"] = new(typeof(int)),
            ["Children"] = new("Person", true)
        });
        var rootSchema = new TypeSchema(new Dictionary<string, TypeSchema>
        {
            ["People"] = new("Person", true),
            ["Founder"] = new("Person", false),
        });

        var dataSourceSchema = new DynamicRecordSchema
        {
            ["Person"] = personSchema,
            ["this"] = rootSchema
        };

        return dataSourceSchema;
    }
    private static DynamicRecord CreateSource(bool founderIsAlice = true)
    {
        var bob = new DynamicRecord { ["Name"] = "Bob", ["Age"] = 15 };
        var carol = new DynamicRecord { ["Name"] = "Carol", ["Age"] = 42 };
        var alice = new DynamicRecord { ["Name"] = "Alice", ["Age"] = 30, ["Children"] = new List<DynamicRecord> { bob, carol } };

        var root = new DynamicRecord
        {
            ["People"] = new List<DynamicRecord> { alice, bob, carol },
            ["Founder"] = founderIsAlice ? alice : carol
        };
        return root;
    }

    internal sealed class DynamicRecord : Dictionary<string, object?>
    {
        public T? GetFieldValue<T>(string fieldName)
        {
            return TryGetValue(fieldName, out var value) && value is T v
                ? v
                : default;
        }
    }
    internal sealed class DynamicRecordSchema : Dictionary<string, TypeSchema>;
    internal sealed class TypeSchema
    {
        public Type? Type { get; }
        public string? Name { get; }
        public IReadOnlyDictionary<string, TypeSchema>? Fields { get; }
        public bool IsCollection { get; }

        public TypeSchema(Type type)
        {
            Type = type;
        }
        public TypeSchema(IReadOnlyDictionary<string, TypeSchema> fields)
        {
            Fields = fields;
        }
        public TypeSchema(string name, bool isCollection)
        {
            Name = name;
            IsCollection = isCollection;
        }
    }
}
