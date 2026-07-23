using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class OptionsTests
{
    [Fact]
    public void SettingScalarProperty_AfterUse_Throws()
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int), "n") } };
        ExpressionParser.Parse("n + 1", options);

        Assert.Throws<InvalidOperationException>(() => options.IgnoreCase = true);
        Assert.Throws<InvalidOperationException>(() => options.ResultType = typeof(int));
        Assert.Throws<InvalidOperationException>(() => options.UseMemberCache = true);
        Assert.Throws<InvalidOperationException>(() => options.Parameters = new List<(Type, string)>());
    }

    [Fact]
    public void MutatingCollection_AfterUse_Throws()
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(int), "n") },
            IncludedTypes = { typeof(Math) }
        };
        ExpressionParser.Parse("n + 1", options);

        Assert.Throws<NotSupportedException>(() => options.Parameters.Add((typeof(int), "m")));
        Assert.Throws<NotSupportedException>(() => options.GlobalMembers.Add("x", (typeof(int), 1)));
        Assert.Throws<NotSupportedException>(() => options.IncludedTypes.Add(typeof(string)));
        Assert.Throws<NotSupportedException>(() => options.StaticImports.Add(typeof(StaticTestClass)));
    }

    [Fact]
    public void FrozenOptions_CanBeReusedAcrossParses()
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int), "n") } };

        var a = (Func<int, int>)ExpressionParser.Parse("n + 1", options).Compile();
        var b = (Func<int, int>)ExpressionParser.Parse("n * 2", options).Compile();

        Assert.Equal(4, a(3));
        Assert.Equal(6, b(3));
    }

    [Fact]
    public void CopyConstructor_ClonesSettings_AndIsMutable()
    {
        var source = new ExpressionParserOptions
        {
            Parameters = { (typeof(int), "n") },
            IncludedTypes = { typeof(Math) },
            StaticImports = { typeof(StaticTestClass) },
            GlobalMembers = { { "b", (typeof(int), 2) } },
            IgnoreCase = true,
            ResultType = typeof(int)
        };
        ExpressionParser.Parse("n + b", source);

        var copy = new ExpressionParserOptions(source);

        Assert.Equal(typeof(int), copy.ResultType);
        Assert.True(copy.IgnoreCase);
        Assert.Contains((typeof(int), "n"), copy.Parameters);
        Assert.Contains(typeof(Math), copy.IncludedTypes);
        Assert.Contains(typeof(StaticTestClass), copy.StaticImports);
        Assert.True(copy.GlobalMembers.ContainsKey("b"));

        // The copy is independent and still mutable
        copy.Parameters.Add((typeof(int), "m"));
        copy.ResultType = typeof(long);
        Assert.Equal(2, copy.Parameters.Count);
        Assert.Equal(typeof(long), copy.ResultType);

        // Mutating the copy did not touch the frozen source
        Assert.Single(source.Parameters);
        Assert.Equal(typeof(int), source.ResultType);
    }

    [Fact]
    public void CopyConstructor_NullSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ExpressionParserOptions(null!));
    }

    [Fact]
    public void BuiltInTypes_Default_ResolvesBuiltInType()
    {
        var options = new ExpressionParserOptions { ResultType = typeof(int) };

        var func = (Func<int>)ExpressionParser.Parse("TimeSpan.FromMinutes(2).Minutes", options).Compile();

        Assert.Equal(2, func());
    }

    [Fact]
    public void IgnoreBuiltInTypes_RejectsBuiltInType()
    {
        var options = new ExpressionParserOptions { IgnoreBuiltInTypes = true };

        Assert.Throws<ExpressionParserException>(() => ExpressionParser.Parse("TimeSpan.FromMinutes(2)", options));
    }

    [Fact]
    public void IgnoreBuiltInTypes_PrimitiveKeywordsStillWork()
    {
        var options = new ExpressionParserOptions { IgnoreBuiltInTypes = true, Parameters = { (typeof(int), "n") } };

        var func = (Func<int, string>)ExpressionParser.Parse("((long)n).ToString()", options).Compile();

        Assert.Equal("5", func(5));
    }

    [Fact]
    public void IgnoreBuiltInTypes_IncludedTypesStillResolve()
    {
        var options = new ExpressionParserOptions { IgnoreBuiltInTypes = true, ResultType = typeof(double), IncludedTypes = { typeof(Math) } };

        var func = (Func<double>)ExpressionParser.Parse("Math.Max(1.0, 2.0)", options).Compile();

        Assert.Equal(2.0, func());
    }

    [Fact]
    public void TypeResolver_ResolvesByShortName()
    {
        var options = new ExpressionParserOptions
        {
            ResultType = typeof(int),
            TypeResolver = name => name == "TestModel" ? typeof(TestModel) : null
        };

        var func = (Func<int>)ExpressionParser.Parse("new TestModel().Value", options).Compile();

        Assert.Equal(1, func());
    }

    [Fact]
    public void TypeResolver_ResolvesByNamespaceQualifiedName()
    {
        var options = new ExpressionParserOptions
        {
            ResultType = typeof(int),
            TypeResolver = name => name == typeof(TestModel).FullName ? typeof(TestModel) : null
        };

        var func = (Func<int>)ExpressionParser.Parse($"new {typeof(TestModel).FullName}().Value", options).Compile();

        Assert.Equal(1, func());
    }

    [Fact]
    public void TypeResolver_ResolvesGenericTypeByArityName()
    {
        string? seen = null;
        var options = new ExpressionParserOptions
        {
            IgnoreBuiltInTypes = true,
            TypeResolver = name =>
            {
                seen = name;
                return name == "List'1" ? typeof(List<>) : null;
            }
        };

        var func = (Func<List<int>>)ExpressionParser.Parse("new List<int>()", options).Compile();

        Assert.Equal("List'1", seen);
        Assert.Empty(func());
    }

    [Fact]
    public void TypeResolver_NotConsultedWhenBuiltInResolves()
    {
        var consulted = false;
        var options = new ExpressionParserOptions
        {
            ResultType = typeof(int),
            TypeResolver = _ => { consulted = true; return null; }
        };

        ExpressionParser.Parse("TimeSpan.FromMinutes(2).Minutes", options);

        Assert.False(consulted);
    }

    [Fact]
    public void CopyConstructor_ClonesBuiltInTypesAndResolver()
    {
        Func<string, Type?> resolver = _ => null;
        var source = new ExpressionParserOptions { IgnoreBuiltInTypes = true, TypeResolver = resolver };

        var copy = new ExpressionParserOptions(source);

        Assert.True(copy.IgnoreBuiltInTypes);
        Assert.Same(resolver, copy.TypeResolver);
    }
}
