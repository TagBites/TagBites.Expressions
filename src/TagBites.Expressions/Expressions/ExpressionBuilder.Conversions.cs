using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TagBites.Expressions.Extensions;
using TagBites.Utils;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    public override Expression? VisitCastExpression(CastExpressionSyntax node)
    {
        var expression = Visit(node.Expression);
        if (expression == null)
            return null;

        var type = ResolveType(node.Type);
        if (type == null)
            return null;

        // (T)default -> default(T)
        if (expression is DelayDefaultExpression)
            return Expression.Default(type);

        // (T)new() -> new T(...)
        if (expression is DelayNewExpression delayNew)
            return ResolveDelayNew(delayNew, type);

        // No conversion between bool and other primitive types
        var sourceType = Nullable.GetUnderlyingType(expression.Type) ?? expression.Type;
        var targetType = Nullable.GetUnderlyingType(type) ?? type;

        if (sourceType == typeof(bool) && IsNonBoolPrimitive(targetType)
            || targetType == typeof(bool) && IsNonBoolPrimitive(sourceType))
        {
            return ToError(node, $"Cannot convert type '{expression.Type.GetFriendlyTypeName()}' to '{type.GetFriendlyTypeName()}'.");
        }

        // Linq Convert has no double-enum conversion
        if (sourceType == typeof(decimal) && targetType.IsEnum || sourceType.IsEnum && targetType == typeof(decimal))
        {
            var underlyingType = Enum.GetUnderlyingType(targetType.IsEnum ? targetType : sourceType);
            if (IsNullableType(expression.Type))
                underlyingType = typeof(Nullable<>).MakeGenericType(underlyingType);

            expression = Expression.Convert(expression, underlyingType);
        }

        return _checkedContext
            ? Expression.ConvertChecked(expression, type)
            : Expression.Convert(expression, type);

        static bool IsNonBoolPrimitive(Type type) => type != typeof(bool) && (type.IsPrimitive || type.IsEnum || type == typeof(decimal));
    }

    private Expression? ToCastOperator(SyntaxNode node, Expression left, Expression right, bool usedByAsOperator)
    {
        var castType = (Type)((ConstantExpression)right).Value;
        var expressionType = left.Type;

        if (usedByAsOperator && castType.IsValueType && !IsNullableType(castType))
            return ToError(node, "The as operator must be used with a reference type or nullable type");

        if (castType.IsAssignableFrom(expressionType) || expressionType.IsAssignableFrom(castType))
            return Expression.Convert(left, castType);

        return ToError(node, $"Cannot convert value type '{left.Type.GetFriendlyTypeName()}' to '{castType.GetFriendlyTypeName()}' using build-in conversion.");
    }
    private Expression? ToAsOperator(SyntaxNode node, Expression left, Expression right)
    {
        // A value type other than the nullable's underlying type is always null, e.g. 200 as long? (C# warns CS0458)
        var castType = (Type)((ConstantExpression)right).Value!;
        if (left.Type.IsValueType && Nullable.GetUnderlyingType(castType) is { } underlying
            && underlying != (Nullable.GetUnderlyingType(left.Type) ?? left.Type))
        {
            return Expression.Block(left, Expression.Constant(null, castType));
        }

        var castOperation = ToCastOperator(node, left, right, true);
        if (castOperation == null)
            return null;

        var condition = ToIsOperator(left, right);

        return Expression.Condition(condition, castOperation, Expression.Constant(null, castOperation.Type));
    }
    private static Expression ToIsOperator(Expression left, Expression right)
    {
        var expressionType = left.Type;

        if (expressionType.IsValueType && !IsNullableType(expressionType))
        {
            var castType = (Type)((ConstantExpression)right).Value;
            castType = Nullable.GetUnderlyingType(castType) ?? castType;
            expressionType = Nullable.GetUnderlyingType(expressionType) ?? expressionType;

            return Expression.MakeBinary(ExpressionType.Equal, Expression.Constant(expressionType), Expression.Constant(castType));
        }

        var leftType = Expression.Call(left, s_objectGetType);

        return Expression.AndAlso(
            ToIsNotNull(left),
            Expression.Call(right, s_typeIsAssignableFrom, leftType));
    }

    private static Expression? TryConvertExpression(Expression expression, Type targetType)
    {
        var sourceType = expression.Type;
        if (sourceType == targetType)
            return expression;

        // The null literal converts to any reference or nullable type
        if (IsNullLiteral(expression) && (!targetType.IsValueType || IsNullableType(targetType)))
            return Expression.Constant(null, targetType);

        if (targetType.IsAssignableFrom(sourceType))
            return ToCast(expression, targetType);

        // A nullable source never converts implicitly to a non-nullable target
        if (TypeUtils.HasImplicitNumericConversion(sourceType, targetType) && !(IsNullableType(sourceType) && !IsNullableType(targetType)))
            return ToCast(expression, targetType);

        var method = FindConversionOperator(sourceType, targetType, "op_Implicit");
        return method != null
            ? Expression.Convert(expression, targetType, method)
            : null;
    }
    private static Expression? TryConvertConstant(Expression expression, Type targetType)
    {
        if (expression is not ConstantExpression constant)
            return null;

        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // A non-negative long constant converts to ulong
        if (constant is { Value: long longValue })
        {
            if (longValue < 0 || target != typeof(ulong))
                return null;

            var ulongConstant = Expression.Constant((ulong)longValue, typeof(ulong));
            return target == targetType ? ulongConstant : (Expression)Expression.Convert(ulongConstant, targetType);
        }

        if (constant is not { Value: int value })
            return null;
        var fits = Type.GetTypeCode(target) switch
        {
            TypeCode.SByte => value is >= sbyte.MinValue and <= sbyte.MaxValue,
            TypeCode.Byte => value is >= byte.MinValue and <= byte.MaxValue,
            TypeCode.Int16 => value is >= short.MinValue and <= short.MaxValue,
            TypeCode.UInt16 => value is >= ushort.MinValue and <= ushort.MaxValue,
            TypeCode.UInt32 or TypeCode.UInt64 => value >= 0,
            _ => false
        };
        if (!fits)
            return null;

        var converted = Expression.Constant(Convert.ChangeType(value, target), target);
        return target == targetType ? converted : Expression.Convert(converted, targetType);
    }

    private static MethodInfo? FindConversionOperator(Type sourceType, Type targetType, string operatorName)
    {
        return FindIn(sourceType) ?? FindIn(targetType);

        MethodInfo? FindIn(Type declaringType)
        {
            // Primitive types conversions are runtime intrinsics, not reflectable methods.
            if (declaringType.IsPrimitive)
                return null;

            foreach (var method in declaringType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != operatorName)
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 1 || !parameters[0].ParameterType.IsAssignableFrom(sourceType))
                    continue;

                if (!targetType.IsAssignableFrom(method.ReturnType))
                    continue;

                return method;
            }

            return null;
        }
    }
    private static Type? FindBestCommonType(List<Type> candidateTypes)
    {
        Type? best = null;
        var count = candidateTypes.Count;

        for (var i = 0; i < count; i++)
        {
            var candidate = candidateTypes[i];
            var isBest = true;

            for (var j = 0; j < count; j++)
            {
                var other = candidateTypes[j];
                if (i != j && !(Nullable.GetUnderlyingType(other) != candidate && IsMatchingParameterType(candidate, other)))
                {
                    isBest = false;
                    break;
                }
            }

            if (isBest)
            {
                if (best != null)
                    return null;

                best = candidate;
            }
        }

        return best;
    }
}
