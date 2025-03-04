using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;

namespace TagBites.Expressions.Tests;

public class ExpressionParserTests
{
    [Theory]
    [InlineData("1 + 2", 3)]
    [InlineData("1 + +2", 3)]
    [InlineData("1 - 2", -1)]
    [InlineData("1 - -2", 3)]
    [InlineData("1 * 2", 2)]
    [InlineData("4 / 2", 2)]
    [InlineData("1d / 2d", 0.5)]
    [InlineData("1.5d * 2d", 3d)]
    [InlineData("5 % 2", 1)]
    public void MathOperators(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 << 2", 4)]
    [InlineData("2 >> 1", 1)]
    [InlineData("1 | 2 | 4", 7)]
    [InlineData("7 & 2", 2)]
    [InlineData("7 ^ 2", 5)]
    public void BitwiseOperators(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 == 2", false)]
    [InlineData("2 == 2", true)]
    [InlineData("1 != 2", true)]
    [InlineData("2 != 2", false)]
    [InlineData("1 < 2", true)]
    [InlineData("2 < 1", false)]
    [InlineData("1 <= 2", true)]
    [InlineData("2 <= 1", false)]
    [InlineData("1 > 2", false)]
    [InlineData("2 > 1", true)]
    [InlineData("1 >= 2", false)]
    [InlineData("2 >= 1", true)]
    [InlineData("!true", false)]
    [InlineData("!false", true)]
    [InlineData("!(1 == 2)", true)]
    public void LogicalOperators(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("(double)1", 1d)]
    [InlineData("(int)2.5", 2)]
    [InlineData("(float)2.5", 2.5f)]
    [InlineData("(double)2.5m", 2.5)]
    public void CastOperators(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 < 2 ? 1 : 2", 1)]
    [InlineData("1 > 2 ? 1 : 2", 2)]
    [InlineData("1 == 2 ? 1 : null", null)]
    [InlineData("1 == 1 ? 1 : null", 1)]
    [InlineData("1 == 2 ? null : 1", 1)]
    [InlineData("1 == 1 ? null : 1", null)]
    public void ConditionalOperator(string script, object? expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 switch { 1 => 10, 2 => 20, _ => 0 }", 10)]
    [InlineData("2 switch { 1 => 10, 2 => 20, _ => 0 }", 20)]
    [InlineData("3 switch { 1 => 10, 2 => 20, _ => 0 }", 0)]
    [InlineData("n switch { 1 => 10, 2 => 20, _ => 0 }", 10)]
    public void Switch(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(int), "n"),
            }
        };

        ExecuteAndTest(script, options, expectedResult, 1);
    }

    [Theory]
    [InlineData(@"""a"" + ""b""", "ab")]
    [InlineData(@"""a"" < ""b""", true)]
    [InlineData(@"""b"" < ""a""", false)]
    [InlineData(@"""a"" <= ""b""", true)]
    [InlineData(@"""b"" <= ""a""", false)]
    [InlineData(@"""a"" > ""b""", false)]
    [InlineData(@"""b"" > ""a""", true)]
    [InlineData(@"""a"" >= ""b""", false)]
    [InlineData(@"""b"" >= ""a""", true)]
    [InlineData(@"""a"" == ""a""", true)]
    [InlineData(@"""a"" == ""b""", false)]
    [InlineData(@"""a"" != ""a""", false)]
    [InlineData(@"""b"" != ""a""", true)]
    [InlineData(@"""a"" + 1", "a1")]
    [InlineData(@"1 + ""a""", "1a")]
    [InlineData(@"'b' + ""a""", "ba")]
    [InlineData(@"(1 == 2 ? 1 : null) + ""a""", "a")]
    [InlineData(@"(1 == 1 ? 1 : null) + ""a""", "1a")]
    [InlineData("$\"{\"a\"}.{\"b\"}\"", "a.b")]
    [InlineData("$\"{1.23:0}x{2.34:00}\"", "1x02")]
    public void StringOperators(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("new DateTime(1992, 8, 7) < new DateTime(2021, 8, 14)", true)]
    [InlineData("new List<int>() != null", true)]
    public void NewOperator(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("new int[] { 1, 2, 3 }[0]", 1)]
    [InlineData("new [] { 1, 2, 3 }[0]", 1)]
    public void Array(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 + 2.1", 3.1)]
    [InlineData("1 + 2L", 3L)]
    [InlineData("2 < 1d", false)]
    [InlineData("2 < 1m", false)]
    [InlineData("2 < 1L", false)]
    [InlineData("2 == 2m", true)]
    [InlineData("1 / 2d", 0.5)]
    public void ImplicitCast(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("new DateTime(2021, 8, 14).Day", 14)]
    [InlineData("new DateTime(2021, 8, 14).Date.Day", 14)]
    [InlineData("DateTime.MinValue < new DateTime(2021, 8, 14)", true)]
    public void AccessMember(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData(@"new DateTime(2021, 8, 14).ToString(""yyyy"")", "2021")]
    [InlineData(@"new DateTime(2021, 8, 14).ToString(""yyyy"").Length", 4)]
    public void Invocation(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("v", 10)]
    [InlineData("v + v", 20)]
    [InlineData(@"v.ToString() + ""-"" + t", "10-ten")]
    [InlineData("int.Parse(v.ToString().Substring(0,1))", 1)]
    [InlineData("m.TimesTen.Value + m.Value", 11)]
    public void Arguments(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(int), "v"),
                (typeof(string), "t"),
                (typeof(TestModel), "m")
            }
        };

        ExecuteAndTest(script, options, expectedResult, 10, "ten", new TestModel());
    }

    [Theory]
    [InlineData("((TimeSpan?)TimeSpan.FromMinutes(2))?.TotalMinutes", 2d)]
    [InlineData("((TimeSpan?)TimeSpan.FromMinutes(2)).Value.TotalMinutes", 2d)]
    [InlineData("nv.Value", 5)]
    [InlineData("m?.TimesTen != null", true)]
    [InlineData("m?.TimesTen?.Value", 10)]
    [InlineData("nv + m.TimesTen?.TimesTen.Value + m.TimesTen?.Value", 115)]
    [InlineData("(1 < 2 ? (int?)1 : 2).Value", 1)]
    [InlineData("(m?.TimesTen.Value ?? nv).Value", 10)]
    public void ConditionalOperators(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(TestModel), "m"),
                (typeof(int?), "nv"),
            }
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel(), 5);
    }

    [Theory]
    [InlineData("list[0]", 1)]
    [InlineData("array[0]", 1)]
    [InlineData("list?[0] ?? 0", 1)]
    [InlineData("(list.Count == array.Length ? list : (IList<int>)array)?[0] ?? 0", 1)]
    public void ItemOperator(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(IList<int>), "list"),
                (typeof(int[]), "array"),
            }
        };

        var list = new List<int> { 1 };
        ExecuteAndTest(script, options, expectedResult, list, list.ToArray());
    }

    [Theory]
    [InlineData("m.ReturnForExactType<int>(2)", 2)]
    [InlineData("m.ReturnForExactType<int>((object)2)", 2)]
    [InlineData("m.ReturnForExactType<long>(2)", 0L)]
    [InlineData("m.GetExactOrDefault<long>(2)", 0L)]
    [InlineData("m.GetExactOrDefault<long>(2, 1)", 1L)]
    [InlineData("m.GetExactOrDefault<long>(2L)", 2L)]
    [InlineData("m.ReturnIt(2)", 2)]
    [InlineData("m.ReturnIt<long>(2)", 2L)]
    public void GenericMethods(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(TestModel), "m") }
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel());
    }

    [Theory]
    [InlineData("Math.Min(2, 2)", 2)]
    [InlineData("Math.Min(2L, 2L)", 2L)]
    [InlineData("Math.Min(2d, 2d)", 2d)]
    [InlineData("Math.Min(2, 2L)", 2L)]
    [InlineData("Math.Min(2L, 2)", 2L)]
    [InlineData("Math.Min(2, 2d)", 2d)]
    [InlineData("Math.Min(2d, 2)", 2d)]
    [InlineData("Math.Min(2d, 2f)", 2d)]
    public void ImplicitCastOnMethodCall(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("Math.Min(2d, 2m)")]
    [InlineData("Math.Min(2m, 2d)")]
    public void AmbiguousMethodCall(string script) => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script, null));

    [Theory]
    [InlineData("m is TestModel", true)]
    [InlineData("m is object", true)]
    [InlineData("!(m is TestModel)", false)]
    [InlineData("m as TestModel != null", true)]
    [InlineData("(int?)1 is int", true)]
    [InlineData("(int?)null is int", false)]
    [InlineData("(int?)1 as int?", 1)]
    [InlineData("(int?)null as int?", null)]
    [InlineData("((ITestModel)m) as TestModel != null", true)]
    public void TypeCheck(string script, object? expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            IncludedTypes =
            {
                typeof(ITestModel)
            },
            Parameters =
            {
                (typeof(TestModel), "m"),
                (typeof(int?), "nv"),
            }
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel(), 5);
    }

    [Theory]
    [InlineData("(int?)null is null", true)]
    [InlineData("(int?)1 is null", false)]
    [InlineData("(int?)1 is not null", true)]
    [InlineData("(int?)null is not null", false)]
    [InlineData("(int?)1 is { }", true)]
    [InlineData("(int?)null is { }", false)]
    [InlineData("(int?)1 is not { }", false)]
    [InlineData("(int?)null is not  { }", true)]
    public void PatternNullCheck(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("(int?)1 is int", true)]
    [InlineData("(int?)null is int", false)]
    [InlineData("true is int", false)]
    public void PatternTypeCheck(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 is 1", true)]
    [InlineData("1 is 0", false)]
    [InlineData("(int?)1 is 1", true)]
    [InlineData("(int?)1 is 0", false)]
    public void PatternConstCheck(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 is 1", true)]
    [InlineData("1 is 0 or 1", true)]
    [InlineData("1 is 1 and > 0", true)]
    public void PatternOrAnd(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 is > 0", true)]
    [InlineData("1 is >= 0", true)]
    [InlineData("1 is < 0", false)]
    [InlineData("1 is <= 0", false)]
    public void PatternRelation(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 is not 0", true)]
    public void PatternUnaryOperator(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("1 is (1)", true)]
    [InlineData("1 is (2 or 1)", true)]
    [InlineData("1 is (1 or 2)", true)]
    [InlineData("1 is not 0 and (1 or 2)", true)]
    [InlineData("1 is not (1 or 2)", false)]
    [InlineData("1 is not (1 or 2 or not 3)", false)]
    [InlineData("(int?)1 is not (null)", true)]
    [InlineData("(int?)1 is (null)", false)]
    [InlineData("(int?)1 is (null or 1)", true)]
    public void PatternParenthesized(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData("(int?)1 is { } a", true)]
    [InlineData("(int?)1 is int a && a == 1", true)]
    [InlineData("(int?)1 is not int a && a == 1", false)]
    [InlineData("(int?)1 is not int a || a == 1", true)]
    [InlineData("(int?)1 is int a && list.Sum(x => x + a) == 9", true)]
    [InlineData("(int?)1 is int a && list.Sum(x => x is int x2 ? x2 + a : 0) == 9", true)]
    public void PatternVar(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            IncludedTypes =
            {
                typeof(ITestModel)
            },
            Parameters =
            {
                (typeof(IList<int>), "list"),
                (typeof(TestModel), "m"),
                (typeof(int?), "nv"),
            }
        };

        ExecuteAndTest(script, options, expectedResult, new List<int> { 1, 2, 3 }, new TestModel(), 5);
    }

    [Theory]
    [InlineData(@"""a"" is { Length: > 0 }", true)]
    [InlineData(@"""a"" is { Length: < 0 }", false)]
    [InlineData(@"s is { X: 1 }", true)]
    [InlineData(@"s is { X: 1, Y: 2 }", true)]
    [InlineData(@"s is { X: 1, Y: 3 }", false)]
    [InlineData(@"s is { X: 3, Y: 2 }", false)]
    [InlineData(@"s is TestStruct { X: 1, Y: 2 } a && a.X + a.Y == 3", true)]
    [InlineData(@"m is { NullChild: null }", true)]
    [InlineData(@"m is { NullChild: not null }", false)]
    [InlineData(@"m is { NullChild: not { } }", true)]
    [InlineData(@"m is { NullChild: { } }", false)]
    [InlineData(@"m is { NullChild: null, TimesTen: { } }", true)]
    [InlineData(@"m is { NullChild: null, TimesTen: not { } }", false)]
    [InlineData(@"m is TestModel { NullChild: null, TimesTen: { } } a && a.TimesTen.Value == 10", true)]
    public void PatternProperty(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(TestStruct), "s"),
                (typeof(TestModel), "m")
            }
        };
        ExecuteAndTest(script, options, expectedResult, new TestStruct { X = 1, Y = 2 }, new TestModel());
    }

    [Theory]
    [InlineData("(1, 2).Item1", 1)]
    [InlineData("(1, 2).Item2", 2)]
    [InlineData("(n, n + 1, a + a).Item1", 1)]
    [InlineData("(n, n + 1, a + a).Item2", 2)]
    [InlineData("(n, n + 1, a + a).Item3", "aa")]
    [InlineData("(A: 1, B: 2).Item1", 1)]
    [InlineData("(A: 1, B: 2).Item2", 2)]
    public void Tuple(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters = {
                (typeof(int), "n"),
                (typeof(string), "a")
            }
        };

        ExecuteAndTest(script, options, expectedResult, 1, "a");
    }

    [Theory]
    [InlineData("TimesTen.Value + Value + v", 16)]
    public void ThisParameter(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(TestModel), "m"),
                (typeof(int), "v")
            },
            UseFirstParameterAsThis = true
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel(), 5);
    }

    [Theory]
    [InlineData("Value", 1)]
    [InlineData("TimesTwo", 2)]
    [InlineData("TimesTen.Value", 10)]
    [InlineData("TimesTwenty.Value", 20)]
    [InlineData("TimesTwenty.TimesTwo", 20 * 2)]
    [InlineData("TimesTwenty.TimesTwenty.Value", 20 * 20)]
    [InlineData("TimesTwenty.TimesTwenty.TimesTwo", 20 * 20 * 2)]
    [InlineData("Value + TimesTwo + TimesTen.Value + TimesTwenty.Value + TimesTwenty.TimesTwo + TimesTwenty.TimesTwenty.Value + TimesTwenty.TimesTwenty.TimesTwo", 1 + 2 + 10 + 20 + 20 * 2 + 20 * 20 + 20 * 20 * 2)]
    [InlineData("m.TimesTwo + m.TimesTwenty.TimesTwo", 2 + 20 * 2)]
    public void DynamicParameterBinding(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(TestModel), "this"),
                (typeof(TestModel), "m")
            },
            UseFirstParameterAsThis = true,
            CustomPropertyResolver = context =>
            {
                if (context.Instance.Type == typeof(TestModel) && TestModel.GetMemberType(context.MemberName) is { } type)
                    return Expression.Convert(
                        Expression.Call(context.Instance, typeof(TestModel).GetMethod("GetValue")!, Expression.Constant(context.MemberName)),
                        type);

                return null;
            },
        };

        ExecuteAndTest(script, options, expectedResult, new TestModel(), new TestModel());
    }

    [Theory]
    [InlineData("TimesTen.Value", "this.TimesTen.Value")]
    [InlineData("m.TimesTen.Value", "m.TimesTen.Value")]
    [InlineData("TimesTen.TimesTwenty.TimesTen.TimesTwenty.TimesTen.TimesTwenty.Value", "this.TimesTen.TimesTwenty.TimesTen.TimesTwenty.TimesTen.TimesTwenty.Value")]
    [InlineData("m.TimesTen.TimesTwenty.TimesTen.TimesTwenty.TimesTen.TimesTwenty.Value", "m.TimesTen.TimesTwenty.TimesTen.TimesTwenty.TimesTen.TimesTwenty.Value")]
    public void FullMemberPathTest(string script, string expectedPath)
    {
        var maxPath = string.Empty;

        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(TestModel), "this"),
                (typeof(TestModel), "m")
            },
            UseFirstParameterAsThis = true,
            CustomPropertyResolver = context =>
            {
                if (context.MemberFullPath?.Length > maxPath.Length)
                    maxPath = context.MemberFullPath;

                if (context.Instance.Type == typeof(TestModel) && TestModel.GetMemberType(context.MemberName) is { } type)
                    return Expression.Convert(
                        Expression.Call(context.Instance, typeof(TestModel).GetMethod("GetValue")!, Expression.Constant(context.MemberName)),
                        type);

                return null;
            },
        };
        ExpressionParser.Parse(script, options);

        Assert.Equal(expectedPath, maxPath);
    }

    [Theory]
    [InlineData("list.First()", 1)]
    [InlineData("list.FirstOrDefault()", 1)]
    [InlineData("list.Count()", 3)]
    [InlineData("list.Min()", 1)]
    [InlineData("list.Max()", 3)]
    [InlineData("list.Sum()", 6)]
    [InlineData("list.First(x => x > 2)", 3)]
    [InlineData("list.Where(x => x > 2).Count()", 1)]
    [InlineData("list.Where((x, i) => x > 1 && i > 1).Count()", 1)]
    [InlineData("array.First(x => x > 2)", 3)]
    [InlineData("models.First(x => x.Value > 1).Value", 10)]
    [InlineData("listOfLists.Select(x => x.Select(y => y * 2).Max()).Sum()", 3 * 2 + 6 * 2)]
    [InlineData("listOfLists.Select(x => x.Select(y => y * 2).Select(x => x * 2).Max()).Sum()", 3 * 2 * 2 + 6 * 2 * 2)]
    [InlineData("list.Sum(x => x + n)", 9)]
    public void Lambda(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(IList<int>), "list"),
                (typeof(int[]), "array"),
                (typeof(IList<IList<int>>), "listOfLists"),
                (typeof(IList<TestModel>), "models"),
                (typeof(int), "n")
            }
        };
        var args = new object[]
        {
            new List<int> { 1, 2, 3 },
            new [] { 1, 2, 3 },
            new List<IList<int>> { new List<int> { 1, 2, 3 }, new List<int> { 4, 5, 6 } },
            new List<TestModel> { new (), new (10), new (100) },
            1
        };
        //var w= new List<IList<int>> { new List<int> { 1, 2, 3 }, new List<int> { 4, 5, 6 } }.Select(x=>x.Select(y=>y.))
        ExecuteAndTest(script, options, expectedResult, args);
    }

    [Theory]
    [InlineData("models.GroupBy(x => (x.K1, x.K2)).Select(x => (x.Key.Item1, x.Key.Item2)).First().Item2", "b")]
    //[InlineData("models.GroupBy(x => (x.K1, x.K2)).Select(x => (x.Key.Item1, x.Key.Item2, x.Sum(y => y.Count))).First().Item3", 6)]
    public void LambdaWithCustomResolver(string script, object expectedResult)
    {
        var t1 = new RuntimeDefinedType
        {
            Properties = { { "K1", typeof(string) }, { "K2", typeof(string) }, { "Count", typeof(int) } }
        };
        var models = new RuntimeDefinedTypeInstanceCollection(t1)
            {
                new (t1) { ["K1"] = "a", ["K2"] = "b", ["Count"] = 1  },
                new (t1) { ["K1"] = "a", ["K2"] = "b", ["Count"] = 2  },
                new (t1) { ["K1"] = "c", ["K2"] = "d", ["Count"] = 3  },
                new (t1) { ["K1"] = "c", ["K2"] = "d", ["Count"] = 4  }
            };

        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(object), "this")
            },
            UseFirstParameterAsThis = true,
            CustomPropertyResolver = CustomPropertyResolver
        };

        ExecuteAndTest(script, options, expectedResult, (object?)null);

        Expression? CustomPropertyResolver(IExpressionMemberResolverContext arg)
        {
            if (arg.MemberFullPath == "this.models")
                return arg.IncludeTypeInfo(Expression.Constant(models), t1);

            if (arg.Instance.Type == typeof(RuntimeDefinedTypeInstance)
                && arg.InstanceTypeInfo is RuntimeDefinedType { } type
                && type.Properties.TryGetValue(arg.MemberName, out var p))
            {
                var method = typeof(RuntimeDefinedTypeInstance)
                    .GetMethod(nameof(RuntimeDefinedTypeInstance.GetTypedValue), BindingFlags.Instance | BindingFlags.Public)!
                    .MakeGenericMethod(p);
                var getValue = Expression.Call(arg.Instance, method, Expression.Constant(arg.MemberName));

                return getValue;
            }

            return null;
        }
    }

    [Theory]
    [InlineData("new TestModel().Value", 1)]
    [InlineData("new TestModel(5).Value", 5)]
    [InlineData("new TestModel { Field1 = 1, Field2 = 2 }.Field1", 1)]
    [InlineData("new TestModel { Field1 = 1, Field2 = 2 }.Field2", 2)]
    [InlineData("new TestModel { Field1 = 0, Field2 = 0 }.Value", 1)]
    [InlineData("new TestModel(5) { Field1 = 1, Field2 = 2 }.Value", 5)]
    public void ObjectCreation(string script, object expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            IncludedTypes =
            {
                typeof(TestModel)
            }
        };
        ExecuteAndTest(script, options, expectedResult);
    }

    [Theory]
    [InlineData("new [] { 1, 2, 3 }.Select(x => (x, x + 1).Item2).Sum()", 9)]
    public void Complex(string script, object expectedResult) => ExecuteAndTest(script, expectedResult);

    [Theory]
    [InlineData(@"new TestModel(1)", typeof(TestModel))]
    public void ResolveTypeUsingResult(string script, Type type)
    {
        var options = new ExpressionParserOptions
        {
            AllowRuntimeCast = true,
            ResultType = type
        };
        var result = Execute(script, options);
        Assert.IsType(type, result);
    }

    [Theory]
    [InlineData(@"typeis(""a"", ""System.String,System.Private.CoreLib"")", true)]
    [InlineData(@"typeis(m, ""System.String,System.Private.CoreLib"")", false)]
    [InlineData(@"typeis(null, ""System.String,System.Private.CoreLib"")", false)]
    [InlineData(@"typeas(""a"", ""System.String,System.Private.CoreLib"")", "a")]
    [InlineData(@"typeas(null, ""System.String,System.Private.CoreLib"")", null)]
    [InlineData(@"typecast(null, ""System.String,System.Private.CoreLib"")", null)]
    [InlineData(@"typecast(""a"", ""System.String,System.Private.CoreLib"")", "a")]
    public void RuntimeCast(string script, object? expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            AllowRuntimeCast = true,
            IncludedTypes =
            {
                typeof(TestModel),
                typeof(ITestModel),
            },
            Parameters = { (typeof(TestModel), "m") }
        };
        ExecuteAndTest(script, options, expectedResult, new TestModel());
    }

    [Fact]
    public void FuncAsParameter()
    {
        var options = new ExpressionParserOptions
        {
            Parameters = {
                (typeof(Func<object?, object?>), "a"),
            }
        };

        Assert.Null(Execute("a?.Invoke(42)", options, [null]));
        Assert.Equal(Execute("a?.Invoke(42)", options, (Func<object, object>)(x => x)), 42);
        Assert.Equal(Execute("a(42)", options, (Func<object, object>)(x => x)), 42);
    }

    [Fact]
    public void ActionAsParameter()
    {
        var options = new ExpressionParserOptions
        {
            Parameters = {
                (typeof(Action<object>), "a")
            },
            IncludedTypes =
            {
                typeof(TestModel)
            }
        };
        var voidCallCount = 0;

        Assert.Null(Execute("a(42)", options, (Action<object>)VoidMethod));
        Assert.Null(Execute("a.Invoke(42)", options, (Action<object>)VoidMethod));
        Assert.Null(Execute("a?.Invoke(42)", options, (Action<object>)VoidMethod));
        Assert.Null(Execute("a?.Invoke(42)", options, [null]));
        Assert.Equal(3, voidCallCount);

        Assert.Null(Execute("TestModel.StaticVoidMethod(42)", options, (Action<object>)VoidMethod));

        void VoidMethod(object x) => ++voidCallCount;
    }

    [Theory]
    [InlineData("GetValue(\"TimesTwo\")", 2)]
    [InlineData("v == 5", true)]
    [InlineData("nv.HasValue && nv == 5", true)]
    [InlineData("Add.Invoke(2, 3)", 5)]
    [InlineData("Add?.Invoke(2, 3)", 5)]
    [InlineData("Add(2, 3)", 5)]
    [InlineData("m.TimesTen.Value", 10)]
    [InlineData("m?.TimesTen.Value", 10)]
    [InlineData("m.GetValue(\"TimesTwo\")", 2)]
    [InlineData("this.GetValue(\"TimesTwo\")", 2)]
    [InlineData("this.TimesTen.Value", 10)]
    [InlineData("TimesTen.Value", 10)]
    [InlineData("ReturnIt<int>(2)", 2)]
    [InlineData("this.ReturnIt<int>(2)", 2)]
    [InlineData("m.ReturnIt<int>(2)", 2)]
    public void GlobalMembers(string script, object? expectedResult)
    {
        var options = new ExpressionParserOptions
        {
            GlobalMembers =
            {
                {"this", (null, new TestModel())},
                {"m", (null, new TestModel())},
                {"v", (null, 5)},
                {"nv", (typeof(int?), 5)},
                {"Add", (null, (Func<int, int, int>)Add)},
            }
        };
        ExecuteAndTest(script, options, expectedResult);

        int Add(int a, int b) => a + b;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1 + * 2")]
    [InlineData("list[]")]
    [InlineData("list[")]
    [InlineData("1 + (")]
    [InlineData("{ 1 }")]
    [InlineData("( 1")]
    [InlineData("list.Func_abs()")]
    [InlineData("list.Min(")]
    [InlineData("list.Min(x=>y)")]
    [InlineData("abc")]
    [InlineData("v + abs")]
    [InlineData("1 switch { \"a\" => 2, _ => 0 }")]
    [InlineData("1 switch { 1 => \"a\", _ => 0 }")]
    [InlineData("1 switch { 1 => 'a', _ => \"a\" }")]
    [InlineData("2d == 2m")]
    [InlineData("2d + 2m")]
    public void InvalidSyntax(string? script)
    {
        var options = new ExpressionParserOptions
        {
            Parameters =
            {
                (typeof(TestModel), "this"),
                (typeof(TestModel), "m"),
                (typeof(IList<int>), "list"),
                (typeof(int), "v")
            },
            UseFirstParameterAsThis = true,
        };

        Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse(script!, options));
    }

    [Theory]
    [InlineData("2.GetType().Name", "Int32", false)]
    [InlineData("2.GetType().IsValueType", true, false)]
    [InlineData("2.GetType().IsAssignableFrom(2.GetType())", true, false)]
    [InlineData("2.GetType().FullName", null, false)]
    [InlineData("2.GetType().Assembly", null, false)]
    [InlineData(@"""a"".GetType().GetProperties().Length > 0", null, false)]
    [InlineData("2.GetType().FullName", "System.Int32", true)]
    [InlineData(@"""a"".GetType().GetProperties().Length > 0", true, true)]
    public void ReflectionCallTest(string script, object? expectedResult, bool allowReflection)
    {
        var options = new ExpressionParserOptions { AllowReflection = allowReflection };
        var shouldParse = expectedResult is not null;

        Assert.Equal(shouldParse, ExpressionParser.TryParse(script, options, out var expression, out _));

        if (expectedResult != null)
        {
            var result = expression!.Compile().DynamicInvoke();
            Assert.Equal(expectedResult, result);
        }
    }

    private static void ExecuteAndTest(string script, object? expectedResult, params object?[] args)
    {
        ExecuteAndTest(script, null, expectedResult, args);
    }
    private static void ExecuteAndTest(string script, ExpressionParserOptions? options, object? expectedResult, params object?[] args)
    {
        var result = Execute(script, options, args);

        Assert.Equal(expectedResult, result);
    }
    private static object? Execute(string script, ExpressionParserOptions? options, params object?[] args)
    {
        var expression = ExpressionParser.Parse(script, options);
        var expressionDelegate = expression.Compile();
        return expressionDelegate.DynamicInvoke(args);
    }

    // ReSharper disable NotAccessedField.Local
    // ReSharper disable UnusedMember.Local
    // ReSharper disable MemberCanBePrivate.Local
    private class ITestModel
    {

    }
    private class TestModel : ITestModel
    {
        private TestModel? _child;
        private TestModel? _dynamiChild;

        public int Value { get; }
        public TestModel TimesTen => _child ??= new TestModel(Value * 10);

        public int Field1 { get; set; }
        public int Field2 { get; set; }

        public TestModel? NullChild => null;

        public TestModel(int value = 1) => Value = value;


        public object? GetValue(string member)
        {
            return member switch
            {
                "TimesTwo" => Value * 2,
                "TimesTen" => TimesTen,
                "TimesTwenty" => _dynamiChild ??= new TestModel(Value * 20),
                _ => null
            };
        }
        public static Type? GetMemberType(string member)
        {
            return member switch
            {
                "TimesTwo" => typeof(int),
                "TimesTen" => typeof(TestModel),
                "TimesTwenty" => typeof(TestModel),
                _ => null
            };
        }

        public T? ReturnForExactType<T>(object v) => v is T v1 ? v1 : default;
        public T ReturnIt<T>(T value) => value;

        public T? GetExactOrDefault<T>(object v, T? defaultValue = default) => v is T v1 ? v1 : defaultValue;

        public static void StaticVoidMethod(object _) { }
    }
    private struct TestStruct
    {
        public int X;
        public int Y;
    }

    private class RuntimeDefinedType
    {
        public Dictionary<string, Type> Properties { get; } = new();
    }
    private class RuntimeDefinedTypeInstance
    {
        public RuntimeDefinedType Type { get; }
        private readonly Dictionary<string, object?> _values = new();

        public object? this[string propertyName]
        {
            get => _values.GetValueOrDefault(propertyName);
            set
            {
                var type = Type.Properties.GetValueOrDefault(propertyName);
                if (type == null || value?.GetType() is { } t && !type.IsAssignableFrom(t))
                    throw new ArgumentException();

                _values[propertyName] = value;
            }
        }

        public RuntimeDefinedTypeInstance(RuntimeDefinedType type) => Type = type;


        public T? GetTypedValue<T>(string propertyName) => this[propertyName] is T t ? t : default;
    }
    private class RuntimeDefinedTypeInstanceCollection : Collection<RuntimeDefinedTypeInstance>
    {
        public RuntimeDefinedType Type { get; }

        public RuntimeDefinedTypeInstanceCollection(RuntimeDefinedType type) => Type = type;


        protected override void InsertItem(int index, RuntimeDefinedTypeInstance item)
        {
            if (item.Type != Type)
                throw new ArgumentException();

            base.InsertItem(index, item);
        }
        protected override void SetItem(int index, RuntimeDefinedTypeInstance item)
        {
            if (item.Type != Type)
                throw new ArgumentException();

            base.SetItem(index, item);
        }
    }

    // ReSharper restore NotAccessedField.Local
    // ReSharper restore UnusedMember.Local
    // ReSharper restore MemberCanBePrivate.Local
}
