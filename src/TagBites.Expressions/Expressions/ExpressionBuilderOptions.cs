using System.Linq.Expressions;

namespace TagBites.Expressions;

internal class ExpressionBuilderOptions
{
    public bool AllowReflection;
    public bool AllowRuntimeCast;
    public bool AllowStringRelationalOperators;
    public bool AllowThrowExpressions;

    public bool IgnoreBuiltInTypes;
    public Func<string, Type?>? TypeResolver;

    public Func<IExpressionMemberResolverContext, Expression?>? CustomPropertyResolver;
}
