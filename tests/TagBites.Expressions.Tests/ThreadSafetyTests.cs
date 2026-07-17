using System.Collections.Concurrent;
using TagBites.Expressions.Tests.Models;

namespace TagBites.Expressions.Tests;

public class ThreadSafetyTests
{
    [Fact]
    public void ConcurrentInvoke_MemberCache()
    {
        var options = new ExpressionParserOptions
        {
            UseMemberCache = true,
            Parameters =
            {
                (typeof(IList<TestModel>), "models"),
                (typeof(TestModel), "m"),
            },
            IncludedTypes = { typeof(TestModelExtensions) }
        };

        // ReSharper disable once RedundantArgumentDefaultValue
        var models = new List<TestModel> { new(1), new(2), new(3) };
        var errors = new ConcurrentBag<Exception>();

        Parallel.For(0, 64, i =>
        {
            try
            {
                switch (i % 4)
                {
                    case 0:
                        Assert.Equal(6, ExpressionParser.Invoke<int>("models.Sum(x => x.Value)", options, models, new TestModel()));
                        break;
                    case 1:
                        Assert.Equal(2, ExpressionParser.Invoke<int>("models.First(x => x.Value > 1).Value", options, models, new TestModel()));
                        break;
                    case 2:
                        Assert.Equal(3, ExpressionParser.Invoke<int>("m.GetValueExtension(3)", options, models, new TestModel()));
                        break;
                    default:
                        Assert.Equal(2, ExpressionParser.Invoke<int>("models.Where(x => x.Value > 1).Count()", options, models, new TestModel()));
                        break;
                }
            }
            catch (Exception e)
            {
                errors.Add(e);
            }
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void ConcurrentInvoke_MemberCache_DifferentExpressions()
    {
        var options = new ExpressionParserOptions
        {
            UseMemberCache = true,
            Parameters = { (typeof(TestModel), "m") }
        };
        var scripts = new[]
        {
            "m.Value",
            "m.Property1",
            "m.ChildTimesTen.Value",
            "m.GetValue(\"TimesTwo\")",
            "m.ReturnArgument<int>(2)",
        };
        var errors = new ConcurrentBag<Exception>();

        Parallel.For(0, 200, i =>
        {
            try
            {
                var lambda = ExpressionParser.Parse(scripts[i % scripts.Length], options);
                lambda.Compile();
            }
            catch (Exception e)
            {
                errors.Add(e);
            }
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void ConcurrentInvokeCompiledDelegate()
    {
        var options = new ExpressionParserOptions { Parameters = { (typeof(int), "n") } };
        var function = ExpressionParser.Compile<Func<int, int>>("n switch { int a when a % 2 == 0 => a * 2, int b => b + 100 }", options);

        var errors = new ConcurrentBag<Exception>();

        Parallel.For(0, 200, i =>
        {
            try
            {
                var expected = i % 2 == 0 ? i * 2 : i + 100;
                Assert.Equal(expected, function(i));
            }
            catch (Exception e)
            {
                errors.Add(e);
            }
        });

        Assert.Empty(errors);
    }
}
