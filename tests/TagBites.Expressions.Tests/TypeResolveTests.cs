using System.Globalization;
using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class TypeResolveTests : ExpressionTestBase
{
    [Theory]
    [InlineData("typeof(TimeSpan)", typeof(TimeSpan))]
    [InlineData("typeof(DateTime)", typeof(DateTime))]
    [InlineData("typeof(DateTimeOffset)", typeof(DateTimeOffset))]
    [InlineData("typeof(DateTimeKind)", typeof(DateTimeKind))]
    [InlineData("typeof(DayOfWeek)", typeof(DayOfWeek))]
    [InlineData("typeof(StringComparison)", typeof(StringComparison))]
    [InlineData("typeof(StringSplitOptions)", typeof(StringSplitOptions))]
    [InlineData("typeof(CultureInfo)", typeof(CultureInfo))]
    [InlineData("typeof(MidpointRounding)", typeof(MidpointRounding))]
    [InlineData("typeof(Math)", typeof(Math))]
    [InlineData("typeof(Guid)", typeof(Guid))]
    [InlineData("typeof(Convert)", typeof(Convert))]
    [InlineData("typeof(Enumerable)", typeof(Enumerable))]
    [InlineData("typeof(KeyValuePair<int, string>)", typeof(KeyValuePair<int, string>))]
    [InlineData("typeof(List<int>)", typeof(List<int>))]
    [InlineData("typeof(Dictionary<int, string>)", typeof(Dictionary<int, string>))]
    [InlineData("typeof(HashSet<int>)", typeof(HashSet<int>))]
    [InlineData("typeof(IList<int>)", typeof(IList<int>))]
    [InlineData("typeof(IEnumerable<int>)", typeof(IEnumerable<int>))]
    [InlineData("typeof(ICollection<int>)", typeof(ICollection<int>))]
    [InlineData("typeof(IReadOnlyList<int>)", typeof(IReadOnlyList<int>))]
    [InlineData("typeof(IReadOnlyCollection<int>)", typeof(IReadOnlyCollection<int>))]
    [InlineData("typeof(IDictionary<int, string>)", typeof(IDictionary<int, string>))]
    [InlineData("typeof(IReadOnlyDictionary<int, string>)", typeof(IReadOnlyDictionary<int, string>))]
    [InlineData("typeof(ISet<int>)", typeof(ISet<int>))]
    public void BuiltInType(string script, Type expectedType)
    {
        // Exists
        ExecuteAndTest(script, expectedType);

        // Rejected
        var options = new ExpressionParserOptions { IgnoreBuiltInTypes = true };
        Assert.Throws<ExpressionParserException>(() => ExpressionParser.Parse(script, options));
    }

    [Theory]
    [InlineData("System.Math.Pow(2, 3)", 8.0)]
    [InlineData("System.Math.PI", Math.PI)]
    [InlineData("System.TimeSpan.FromMinutes(2).Minutes", 2)]
    [InlineData("System.Math.Max(1.0, System.Math.Min(2.0, 3.0))", 2.0)]
    [InlineData("System.Math.PI > 3.0", true)]
    [InlineData("typeof(System.Guid) == typeof(Guid)", true)]
    public void NamespaceQualified(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("System.Text.Math.Pow(2, 3)")]
    [InlineData("System.NotAType.Foo()")]
    [InlineData("new System.Unknown()")]
    [InlineData("new Unknown()")]
    public void UnresolvedType_Throws(string script)
    {
        Assert.Throws<ExpressionParserException>(() => ExpressionParser.Parse(script, null));
    }

    [Fact]
    public void IgnoreBuiltInTypes_PrimitiveKeywordsStillWork()
    {
        var options = new ExpressionParserOptions { IgnoreBuiltInTypes = true, Parameters = { (typeof(int), "n") } };

        ExecuteAndTest("((long)n).ToString()", options, "5", 5);
    }

    [Fact]
    public void IgnoreBuiltInTypes_IncludedTypesStillResolve()
    {
        var options = new ExpressionParserOptions { IgnoreBuiltInTypes = true, IncludedTypes = { typeof(Math) } };

        ExecuteAndTest("Math.Max(1.0, 2.0)", options, 2.0);
    }

    [Fact]
    public void IncludedTypes_ResolvesByName()
    {
        var options = new ExpressionParserOptions { ResultType = typeof(int), IncludedTypes = { typeof(TestModel) } };

        ExecuteAndTest("new TestModel().Value", options, 1);
    }

    [Fact]
    public void TypeResolver_ResolvesByShortName()
    {
        var options = new ExpressionParserOptions { ResultType = typeof(int), TypeResolver = name => name == "TestModel" ? typeof(TestModel) : null };

        ExecuteAndTest("new TestModel().Value", options, 1);
    }

    [Fact]
    public void TypeResolver_ResolvesByQualifiedName()
    {
        var options = new ExpressionParserOptions { ResultType = typeof(int), TypeResolver = name => name == typeof(TestModel).FullName ? typeof(TestModel) : null };

        ExecuteAndTest($"new {typeof(TestModel).FullName}().Value", options, 1);
    }

    [Fact]
    public void TypeResolver_ReceivesFullNameForQualifiedAccess()
    {
        string? seen = null;
        var options = new ExpressionParserOptions
        {
            IgnoreBuiltInTypes = true,
            TypeResolver = name => { seen = name; return name == "System.Math" ? typeof(Math) : null; }
        };

        ExecuteAndTest("System.Math.Pow(2, 3)", options, 8.0);
        Assert.Equal("System.Math", seen);
    }

    [Fact]
    public void TypeResolver_ResolvesOpenGenericByArityName()
    {
        string? seen = null;
        var options = new ExpressionParserOptions
        {
            IgnoreBuiltInTypes = true,
            TypeResolver = name => { seen = name; return name == "List'1" ? typeof(List<>) : null; }
        };

        var func = (Func<List<int>>)ExpressionParser.Parse("new List<int>()", options).Compile();

        Assert.Equal("List'1", seen);
        Assert.Empty(func());
    }

    [Fact]
    public void TypeResolver_NotConsultedWhenBuiltInResolves()
    {
        var consulted = false;
        var options = new ExpressionParserOptions { ResultType = typeof(int), TypeResolver = _ => { consulted = true; return null; } };

        ExpressionParser.Parse("TimeSpan.FromMinutes(2).Minutes", options);

        Assert.False(consulted);
    }
}
