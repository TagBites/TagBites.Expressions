using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TagBites.Expressions.Extensions;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    public override Expression? VisitDefaultExpression(DefaultExpressionSyntax node)
    {
        var type = ResolveType(node.Type);
        return type != null
            ? Expression.Default(type)
            : null;
    }

    private static Expression CreateDefaultArgument(ParameterInfo parameter)
    {
        var parameterType = parameter.ParameterType;

        if (parameterType.ContainsGenericParameters)
            return new DelayDefaultExpression();

        var defaultValue = parameter.DefaultValue;
        if (defaultValue == null && parameterType.IsValueType)
            defaultValue = Activator.CreateInstance(parameterType);

        return Expression.Constant(defaultValue, parameterType);
    }
}
