using System.Linq.Expressions;
using System.Reflection;
using TagBites.Expressions.Extensions;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    internal static Expression ToIsNotNull(Expression expression)
    {
        if (Nullable.GetUnderlyingType(expression.Type) is { })
            return Expression.MakeMemberAccess(expression, expression.Type.GetProperty("HasValue")!);

        if (expression.Type.IsValueType)
            return Expression.Constant(true);

        return Expression.NotEqual(expression, Expression.Constant(null, expression.Type));
    }
    private static bool IsNullLiteral(Expression expression) => expression is ConstantExpression { Value: null } && expression.Type == typeof(object);

    private static Expression ToCast(Expression expression, Type type) => expression.Type != type ? Expression.Convert(expression, type) : expression;
    private static Expression CallWhenNotNull(Expression instance, MethodInfo method)
    {
        if (Nullable.GetUnderlyingType(instance.Type) is { })
            return new ConditionalAccessExpression(instance, Expression.Call(Expression.MakeMemberAccess(instance, instance.Type.GetProperty("Value")!), method)).Reduce();

        return instance.Type.IsValueType
            ? Expression.Call(instance, method)
            : new ConditionalAccessExpression(instance, Expression.Call(instance, method)).Reduce();
    }

}
