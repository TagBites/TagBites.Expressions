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
    public void CopyConstructor_ClonesBuiltInTypesAndResolver()
    {
        Func<string, Type?> resolver = _ => null;
        var source = new ExpressionParserOptions { IgnoreBuiltInTypes = true, TypeResolver = resolver };

        var copy = new ExpressionParserOptions(source);

        Assert.True(copy.IgnoreBuiltInTypes);
        Assert.Same(resolver, copy.TypeResolver);
    }

    [Fact]
    public void Fork_OverridesParameters_AndInheritsSharedTypes()
    {
        var options = new ExpressionParserOptions { IncludedTypes = { typeof(Math) } };
        ExpressionParser.Parse("Math.Abs(-1)", options);

        var fork = options.Fork(parameters: new List<(Type, string)> { (typeof(int), "n") });

        var f = (Func<int, int>)ExpressionParser.Parse("n + (int)Math.Abs(-2)", fork).Compile();
        Assert.Equal(5, f(3));
    }

    [Fact]
    public void Fork_InheritsParametersWhenNotOverridden()
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int), "n") } };

        var fork = options.Fork(resultType: typeof(long));

        var f = (Func<int, long>)ExpressionParser.Parse("n + 1", fork).Compile();
        Assert.Equal(4L, f(3));
    }

    [Fact]
    public void Fork_InheritsSharedSettings()
    {
        var options = new ExpressionParserOptions { IgnoreCase = true, IncludedTypes = { typeof(Math) } };

        var fork = options.Fork(resultType: typeof(double));

        Assert.True(fork.IgnoreCase);
        Assert.Equal(2d, ExpressionParser.Invoke<double>("math.sqrt(4)", fork));
    }

    [Fact]
    public void Fork_IsReadOnly()
    {
        var options = new ExpressionParserOptions();

        var fork = options.Fork(resultType: typeof(int));

        Assert.Throws<InvalidOperationException>(() => fork.ResultType = typeof(long));
        Assert.Throws<InvalidOperationException>(() => fork.IgnoreCase = true);
        Assert.Throws<InvalidOperationException>(() => fork.Parameters = new List<(Type, string)>());
    }

    [Fact]
    public void Fork_WithoutOverrides_ReturnsSameInstance()
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int), "n") } };

        Assert.Same(options, options.Fork());
        Assert.Throws<InvalidOperationException>(() => options.ResultType = typeof(int));
    }

    [Fact]
    public void Fork_DoesNotMutateSource()
    {
        var options = new ExpressionParserOptions { ResultType = typeof(int), Parameters = { (typeof(int), "n") } };

        var fork = options.Fork(resultType: typeof(long), parameters: new List<(Type, string)> { (typeof(string), "s") });

        Assert.Equal(typeof(int), options.ResultType);
        Assert.Single(options.Parameters);
        Assert.Equal((typeof(int), "n"), options.Parameters[0]);
        Assert.Equal(typeof(long), fork.ResultType);
    }
}
