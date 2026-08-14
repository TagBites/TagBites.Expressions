using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CodeAnalysis;
using TagBites.Utils;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    private bool HasConstantConversionError(SyntaxNode node, Expression operand, Type targetType)
    {
        if (_checkedContext == false || TryGetConstantValue(operand) is not { } value)
            return false;

        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (!IsIntegralType(target) || !IsIntegralType(value.GetType()))
            return false;

        try
        {
            _ = ConvertIntegral(value, target, detectOverflow: true);
            return false;
        }
        catch (OverflowException)
        {
            ToError(node, $"Constant value '{value}' cannot be converted to a '{target.GetFriendlyTypeName()}' (use 'unchecked' syntax to override).");
            return true;
        }
    }
    private bool HasConstantOperationError(SyntaxNode node, ExpressionType expressionType, Expression left, Expression right)
    {
        if (expressionType is not (ExpressionType.Add or ExpressionType.Subtract or ExpressionType.Multiply)
            || _checkedContext == false
            || TryGetConstantValue(left) is not { } leftValue
            || TryGetConstantValue(right) is not { } rightValue
            || leftValue.GetType() != rightValue.GetType()
            || !IsIntegralType(leftValue.GetType()))
        {
            return false;
        }

        try
        {
            _ = ComputeIntegral(expressionType, leftValue, rightValue, detectOverflow: true);
            return false;
        }
        catch (OverflowException)
        {
            ToError(node, "The operation overflows at compile time in checked mode.");
            return true;
        }
    }

    /// <remarks>
    /// Every node here was already accepted by the overflow checks above, so the arithmetic wraps like the runtime does.
    /// </remarks>
    private static object? TryGetConstantValue(Expression expression)
    {
        switch (expression)
        {
            case ConstantExpression { Value: { } value }:
                return value;

            // A const field is not folded into a constant: int.MaxValue stays a field access
            case MemberExpression { Expression: null, Member: FieldInfo { IsLiteral: true } field }:
                return field.GetRawConstantValue();

            case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary:
                {
                    var operand = TryGetConstantValue(unary.Operand);
                    var target = Nullable.GetUnderlyingType(unary.Type) ?? unary.Type;
                    return operand != null && IsIntegralType(target) && IsIntegralType(operand.GetType())
                        ? ConvertIntegral(operand, target, detectOverflow: false)
                        : null;
                }

            case UnaryExpression { NodeType: ExpressionType.Negate or ExpressionType.NegateChecked } unary:
                {
                    var operand = TryGetConstantValue(unary.Operand);
                    return operand != null && IsIntegralType(operand.GetType())
                        ? NegateIntegral(operand, detectOverflow: false)
                        : null;
                }

            case BinaryExpression binary:
                {
                    var leftValue = TryGetConstantValue(binary.Left);
                    var rightValue = TryGetConstantValue(binary.Right);
                    return leftValue != null && rightValue != null
                           && leftValue.GetType() == rightValue.GetType()
                           && IsIntegralType(leftValue.GetType())
                        ? ComputeIntegral(binary.NodeType, leftValue, rightValue, detectOverflow: false)
                        : null;
                }

            default:
                return null;
        }
    }

    private static bool IsIntegralType(Type type)
    {
        return Type.GetTypeCode(type) is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
            or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Char;
    }
    private static object? ConvertIntegral(object value, Type target, bool detectOverflow)
    {
        if (!detectOverflow)
        {
            // A ulong above long.MaxValue keeps its bit pattern
            var bits = value is ulong raw ? unchecked((long)raw) : Convert.ToInt64(value);

            return Type.GetTypeCode(target) switch
            {
                TypeCode.SByte => unchecked((sbyte)bits),
                TypeCode.Byte => unchecked((byte)bits),
                TypeCode.Int16 => unchecked((short)bits),
                TypeCode.UInt16 => unchecked((ushort)bits),
                TypeCode.Int32 => unchecked((int)bits),
                TypeCode.UInt32 => unchecked((uint)bits),
                TypeCode.Int64 => bits,
                TypeCode.UInt64 => unchecked((ulong)bits),
                TypeCode.Char => unchecked((char)bits),
                _ => null
            };
        }

        return Type.GetTypeCode(target) switch
        {
            TypeCode.SByte => checked((sbyte)Convert.ToInt64(value)),
            TypeCode.Byte => checked((byte)Convert.ToInt64(value)),
            TypeCode.Int16 => checked((short)Convert.ToInt64(value)),
            TypeCode.UInt16 => checked((ushort)Convert.ToInt64(value)),
            TypeCode.Int32 => checked((int)Convert.ToInt64(value)),
            TypeCode.Char => checked((char)Convert.ToInt64(value)),
            TypeCode.Int64 => Convert.ToInt64(value),
            TypeCode.UInt32 => value is ulong or long ? checked((uint)Convert.ToUInt64(value)) : checked((uint)Convert.ToInt64(value)),
            TypeCode.UInt64 => value is ulong ? value : checked((ulong)Convert.ToInt64(value)),
            _ => null
        };
    }
    private static object? NegateIntegral(object value, bool detectOverflow)
    {
        return value switch
        {
            int i => detectOverflow ? checked(-i) : unchecked(-i),
            long l => detectOverflow ? checked(-l) : unchecked(-l),
            _ => null
        };
    }
    private static object? ComputeIntegral(ExpressionType expressionType, object left, object right, bool detectOverflow)
    {
        return left switch
        {
            int a when right is int b => ComputeIntegral(expressionType, a, b, detectOverflow),
            uint a when right is uint b => ComputeIntegral(expressionType, a, b, detectOverflow),
            long a when right is long b => ComputeIntegral(expressionType, a, b, detectOverflow),
            ulong a when right is ulong b => ComputeIntegral(expressionType, a, b, detectOverflow),
            _ => null
        };
    }
    private static object? ComputeIntegral(ExpressionType expressionType, int a, int b, bool detectOverflow)
    {
        return expressionType switch
        {
            ExpressionType.Add => detectOverflow ? checked(a + b) : unchecked(a + b),
            ExpressionType.Subtract => detectOverflow ? checked(a - b) : unchecked(a - b),
            ExpressionType.Multiply => detectOverflow ? checked(a * b) : unchecked(a * b),
            _ => null
        };
    }
    private static object? ComputeIntegral(ExpressionType expressionType, uint a, uint b, bool detectOverflow)
    {
        return expressionType switch
        {
            ExpressionType.Add => detectOverflow ? checked(a + b) : unchecked(a + b),
            ExpressionType.Subtract => detectOverflow ? checked(a - b) : unchecked(a - b),
            ExpressionType.Multiply => detectOverflow ? checked(a * b) : unchecked(a * b),
            _ => null
        };
    }
    private static object? ComputeIntegral(ExpressionType expressionType, long a, long b, bool detectOverflow)
    {
        return expressionType switch
        {
            ExpressionType.Add => detectOverflow ? checked(a + b) : unchecked(a + b),
            ExpressionType.Subtract => detectOverflow ? checked(a - b) : unchecked(a - b),
            ExpressionType.Multiply => detectOverflow ? checked(a * b) : unchecked(a * b),
            _ => null
        };
    }
    private static object? ComputeIntegral(ExpressionType expressionType, ulong a, ulong b, bool detectOverflow)
    {
        return expressionType switch
        {
            ExpressionType.Add => detectOverflow ? checked(a + b) : unchecked(a + b),
            ExpressionType.Subtract => detectOverflow ? checked(a - b) : unchecked(a - b),
            ExpressionType.Multiply => detectOverflow ? checked(a * b) : unchecked(a * b),
            _ => null
        };
    }
}
