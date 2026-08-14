using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TagBites.Expressions.Extensions;
using TagBites.Utils;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    public override Expression? VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        var condition = Visit(node.Condition);
        if (condition == null)
            return null;

        var whenTrue = Visit(node.WhenTrue);
        if (whenTrue == null)
            return null;

        var whenFalse = Visit(node.WhenFalse);
        if (whenFalse == null)
            return null;

        if (IsNullLiteral(whenTrue) && IsNullLiteral(whenFalse))
            return ToError(node, "Type of conditional expression cannot be determined because there is no implicit conversion between '<null>' and '<null>'.");

        if (!EnsureTheSameTypes(node, ref whenTrue, ref whenFalse, bestCommonOnly: true))
            return null;

        var result = Expression.Condition(condition, whenTrue, whenFalse);

        // Tuple names survive only where both branches agree
        if (_tupleShapes != null)
            SetTupleShape(result, ValueTupleShape.MergeShapes(GetTupleShape(whenTrue), GetTupleShape(whenFalse), _nameComparison));

        return result;
    }
    public override Expression? VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        var governing = Visit(node.GoverningExpression);
        if (governing == null)
            return null;

        // Create paths
        var paths = new List<(Expression When, Expression Then)>();
        Expression? switchExpression = null!;

        for (var i = 0; i < node.Arms.Count; i++)
        {
            var arm = node.Arms[i];
            Expression? condition = null;

            // Pattern variables are scoped to their own arm, so each arm starts from the same variable set
            var variablesBefore = _variables?.Count ?? 0;

            switch (arm.Pattern)
            {
                case DiscardPatternSyntax when i + 1 != node.Arms.Count || switchExpression != null:
                    return ToError(node, "Invalid switch syntax.");
                case DiscardPatternSyntax:
                    break;

                case ConstantPatternSyntax { Expression: not LiteralExpressionSyntax { Token.Text: "null" } } cps:
                    {
                        var value = Visit(cps.Expression);
                        if (value == null)
                            return null;

                        // Type name is a pattern, not a constant
                        if (TryGetPatternType(value) is { } armType)
                        {
                            condition = ToTypePattern(arm.Pattern, governing, armType);
                            if (condition == null)
                                return null;

                            break;
                        }

                        if (!IsConstantPatternValue(value))
                            return ToError(arm.Pattern, $"A constant value of type '{value.Type.GetFriendlyTypeName()}' is expected.");

                        if (IsNaNConstant(value))
                        {
                            condition = ToNaNPattern(governing, value.Type);
                            break;
                        }

                        if (governing.Type == typeof(object) && value.Type.IsValueType)
                        {
                            condition = ToBoxedConstantPattern(governing, value);
                            break;
                        }

                        if (!EnsureArgumentType(governing.Type, ref value))
                            return ToError(arm.Pattern, "Switch governing and arm type mismatch.");

                        condition = Expression.MakeBinary(ExpressionType.Equal, governing, value);
                        break;
                    }

                default:
                    {
                        condition = ResolvePattern(governing, arm.Pattern);
                        if (condition == null)
                            return null;
                        break;
                    }
            }

            // Case guard
            if (arm.WhenClause != null)
            {
                var guard = Visit(arm.WhenClause.Condition);
                if (guard == null)
                    return null;

                if ((Nullable.GetUnderlyingType(guard.Type) ?? guard.Type) != typeof(bool))
                    return ToError(arm.WhenClause, "Expected boolean expression.");

                condition = condition != null ? Expression.AndAlso(condition, guard) : guard;
            }

            var expression = Visit(arm.Expression);
            if (expression == null)
                return null;

            if (condition == null)
                switchExpression = expression;
            else
                paths.Add((condition, expression));

            // Drop this arm's pattern variables so a later arm can declare the same names
            if (_variables != null && _variables.Count > variablesBefore)
                _variables.RemoveRange(variablesBefore, _variables.Count - variablesBefore);
        }

        // No discard arm, a fallback arm throwing when nothing matches
        if (switchExpression == null)
        {
            if (paths.Count == 0)
                return ToError(node);

            var resultArm = paths.FindLast(x => x.Then is not DelayThrowExpression);
            if (resultArm.Then == null)
                return ToError(node, "Cannot infer the switch type when every arm throws.");

            var exceptionConstructor = typeof(InvalidOperationException).GetConstructor([typeof(string)])!;
            switchExpression = Expression.Throw(
                Expression.New(exceptionConstructor, Expression.Constant("The input was not matched by any switch expression arm.")),
                resultArm.Then.Type);
        }

        // Convert to if-else
        for (var i = paths.Count - 1; i >= 0; i--)
        {
            var then = paths[i].Then;
            if (!EnsureTheSameTypes(node.Arms[i].Expression, ref then, ref switchExpression, bestCommonOnly: true))
                return null;

            if (switchExpression.Type != then.Type)
                return ToError(node.Arms[i].Expression, "Switch expressions types mismatch.");

            switchExpression = Expression.Condition(paths[i].When, then, switchExpression);
        }

        return switchExpression;
    }

    public override Expression? VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        var left = Visit(node.Left);
        if (left == null)
            return null;

        // Roslyn parses `x is Enum.Member` as the is-type operator with a qualified name; when it names a constant it is an equality test
        if ((SyntaxKind)node.OperatorToken.RawKind == SyntaxKind.IsKeyword
            && node.Right is QualifiedNameSyntax { Right: IdentifierNameSyntax constantMember } constantName
            && (constantName.Left is IdentifierNameSyntax constantType
                ? TryResolveTypeByName(constantType.Identifier.Text)
                : TryResolveNamespaceQualifiedType(constantName.Left)) is { } declaringType
            && ResolveMember(node.Right, Expression.Constant(declaringType), constantMember.Identifier.Text, setErrorWhenNotFound: false) is { } constantValue)
        {
            if (!IsConstantPatternValue(constantValue))
                return ToError(node.Right, $"A constant value of type '{constantValue.Type.GetFriendlyTypeName()}' is expected.");

            if (left.Type == typeof(object) && constantValue.Type.IsValueType)
                return ToBoxedConstantPattern(left, constantValue);

            if (!EnsureTheSameTypes(node, ref left, ref constantValue))
                return null;

            return Expression.MakeBinary(ExpressionType.Equal, left, constantValue);
        }

        // is/as with a generic, array or qualified type - resolve the right side as a type
        if ((SyntaxKind)node.OperatorToken.RawKind is SyntaxKind.IsKeyword or SyntaxKind.AsKeyword
            && node.Right is GenericNameSyntax or ArrayTypeSyntax or QualifiedNameSyntax)
        {
            var type = ResolveType((TypeSyntax)node.Right);
            if (type == null)
                return null;

            return (SyntaxKind)node.OperatorToken.RawKind == SyntaxKind.IsKeyword
                ? ToIsOperator(left, Expression.Constant(type))
                : ToAsOperator(node, left, Expression.Constant(type));
        }

        var right = Visit(node.Right);
        if (right == null)
            return null;

        if (left is DelayDefaultExpression || right is DelayDefaultExpression)
        {
            if (left is DelayDefaultExpression && right is DelayDefaultExpression)
                return ToError(node, "Cannot infer the type of 'default' when both operands are 'default'.");

            if (left is DelayDefaultExpression)
                left = Expression.Default(right.Type);
            else
                right = Expression.Default(left.Type);
        }

        var expressionType = (SyntaxKind)node.OperatorToken.RawKind switch
        {
            // Math
            SyntaxKind.PlusToken => ExpressionType.Add,
            SyntaxKind.MinusToken => ExpressionType.Subtract,
            SyntaxKind.AsteriskToken => ExpressionType.Multiply,
            SyntaxKind.SlashToken => ExpressionType.Divide,
            SyntaxKind.PercentToken => ExpressionType.Modulo,

            // Bitwise
            SyntaxKind.BarToken => ExpressionType.Or,
            SyntaxKind.AmpersandToken => ExpressionType.And,
            SyntaxKind.CaretToken => ExpressionType.ExclusiveOr,
            SyntaxKind.LessThanLessThanToken => ExpressionType.LeftShift,
            SyntaxKind.GreaterThanGreaterThanToken => ExpressionType.RightShift,

            // Logic
            SyntaxKind.BarBarToken => ExpressionType.OrElse,
            SyntaxKind.AmpersandAmpersandToken => ExpressionType.AndAlso,

            SyntaxKind.EqualsEqualsToken => ExpressionType.Equal,
            SyntaxKind.ExclamationEqualsToken => ExpressionType.NotEqual,
            SyntaxKind.GreaterThanToken => ExpressionType.GreaterThan,
            SyntaxKind.GreaterThanEqualsToken => ExpressionType.GreaterThanOrEqual,
            SyntaxKind.LessThanToken => ExpressionType.LessThan,
            SyntaxKind.LessThanEqualsToken => ExpressionType.LessThanOrEqual,

            // Cast
            SyntaxKind.IsKeyword => ExpressionType.TypeIs,
            SyntaxKind.AsKeyword => ExpressionType.TypeAs,

            _ => ExpressionType.Throw
        };

        // Special operators
        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if (expressionType == ExpressionType.Throw)
        {
            // operator ??
            if ((SyntaxKind)node.OperatorToken.RawKind == SyntaxKind.QuestionQuestionToken)
            {
                if (left is DelayNewExpression or DelayThrowExpression)
                    return ToError(node, "Cannot infer the type of the left operand here.");

                if (IsNullLiteral(left) && IsNullLiteral(right))
                    return ToError(node, "Operator '??' cannot be applied to operands of type '<null>' and '<null>'.");

                if (left.Type.IsValueType && !IsNullableType(left.Type))
                    return ToError(node, $"Operator '??' cannot be applied to an operand of type '{left.Type.GetFriendlyTypeName()}'.");

                var condition = left;

                if (IsNullableType(left.Type))
                {
                    var unwrapped = Expression.MakeMemberAccess(left, left.Type.GetProperty(nameof(Nullable<>.Value))!);

                    if (right is not DelayThrowExpression and not DelayNewExpression and not DelayDefaultExpression)
                    {
                        // (long?)5L ?? (int?)7 is long?
                        if ((TryConvertExpression(right, unwrapped.Type) ?? TryConvertConstant(right, unwrapped.Type)) is { } rightAsUnderlying)
                            return Expression.Condition(ToIsNotNull(condition), unwrapped, rightAsUnderlying);

                        if (right.Type != left.Type && (TryConvertExpression(right, left.Type) ?? TryConvertConstant(right, left.Type)) is { } rightAsNullable)
                            return Expression.Condition(ToIsNotNull(condition), left, rightAsNullable);
                    }

                    left = unwrapped;
                }

                if (!EnsureTheSameTypes(node, ref left, ref right, bestCommonOnly: true))
                    return null;

                return Expression.Condition(ToIsNotNull(condition), left, right);
            }

            // Unknown
            return ToError(node, $"Unsupported binary operator '{node.OperatorToken.ValueText}'.");
        }

        // Target-typed new() and throw have no type of their own for a value operator
        if (left is DelayNewExpression or DelayThrowExpression || right is DelayNewExpression or DelayThrowExpression)
            return ToError(node, "Cannot infer type for the operand here.");

        // is operator
        if (expressionType == ExpressionType.TypeIs)
        {
            return TryGetPatternType(right) != null
                ? ToIsOperator(left, right)
                : ToError(node.Right, $"'{node.Right}' is not a type or a constant value.");
        }

        // as operator
        if (expressionType == ExpressionType.TypeAs)
            return ToAsOperator(node, left, right);

        // Operator + is not defined for string - use Contact instead
        if (expressionType == ExpressionType.Add && (left.Type == typeof(string) || right.Type == typeof(string)))
        {
            if (left.Type != typeof(string))
                left = CallWhenNotNull(left, s_objectToString);

            if (right.Type != typeof(string))
                right = CallWhenNotNull(right, s_objectToString);

            return Expression.Call(null, s_stringConcatObject, left, right);
        }

        // Operator < <= > >= is not defined for string - use Compare instead (opt-in, not valid in real C#)
        if (_options.AllowStringRelationalOperators
            && left.Type == typeof(string) && right.Type == typeof(string)
            && expressionType is ExpressionType.LessThan or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual)
        {
            var compareExpression = Expression.Call(null, s_stringCompare, left, right);
            return Expression.MakeBinary(expressionType, compareExpression, Expression.Constant(0));
        }

        // Operator || && for plain bool only - C# defines only & and | for bool?
        if (expressionType is ExpressionType.AndAlso or ExpressionType.OrElse)
        {
            if (left.Type != typeof(bool))
                return ToError(node.Left, "Expected boolean expression.");

            if (right.Type != typeof(bool))
                return ToError(node.Right, "Expected boolean expression.");
        }

        // C# reference equality does not box, so object compared to a value type is an error
        if (expressionType is ExpressionType.Equal or ExpressionType.NotEqual
            && !IsNullLiteral(left) && !IsNullLiteral(right)
            && (left.Type == typeof(object) && right.Type.IsValueType || right.Type == typeof(object) && left.Type.IsValueType))
        {
            return ToError(node, $"Operator '{node.OperatorToken.ValueText}' cannot be applied to operands of type '{left.Type.GetFriendlyTypeName()}' and '{right.Type.GetFriendlyTypeName()}'.");
        }

        // Enum arithmetic
        if ((Nullable.GetUnderlyingType(left.Type) ?? left.Type).IsEnum || (Nullable.GetUnderlyingType(right.Type) ?? right.Type).IsEnum)
            return BuildEnumBinaryOperation(node, expressionType, left, right);

        // Tuple has no == operator
        if (expressionType is ExpressionType.Equal or ExpressionType.NotEqual
            && IsValueTupleType(left.Type) && IsValueTupleType(right.Type))
        {
            if (left.Type == right.Type)
            {
                var equalsCall = Expression.Call(left, left.Type.GetMethod(nameof(Equals), [left.Type])!, right);
                return expressionType == ExpressionType.Equal ? equalsCall : Expression.Not(equalsCall);
            }

            // Different element types compare element-wise with implicit conversions
            var leftVariable = Expression.Variable(left.Type);
            var rightVariable = Expression.Variable(right.Type);
            var comparison = BuildTupleEquality(node, leftVariable, rightVariable);
            if (comparison == null)
                return null;

            if (expressionType == ExpressionType.NotEqual)
                comparison = Expression.Not(comparison);

            return Expression.Block([leftVariable, rightVariable],
                Expression.Assign(leftVariable, left),
                Expression.Assign(rightVariable, right),
                comparison);
        }

        // C# promotes operands smaller than int (byte/sbyte/short/ushort/char) to int before applying the operator,
        // except against uint/ulong, where byte/ushort/char convert to the unsigned type directly (e.g. (byte)3 + 5ul is ulong)
        var leftIsUnsigned = (Nullable.GetUnderlyingType(left.Type) ?? left.Type) is var lu && (lu == typeof(uint) || lu == typeof(ulong));
        var rightIsUnsigned = (Nullable.GetUnderlyingType(right.Type) ?? right.Type) is var ru && (ru == typeof(uint) || ru == typeof(ulong));

        if (!rightIsUnsigned)
            left = PromoteSmallInteger(left);
        if (!leftIsUnsigned)
            right = PromoteSmallInteger(right);

        // For shift operators the shift count is always int
        if (expressionType is ExpressionType.LeftShift or ExpressionType.RightShift)
        {
            if (right.Type != typeof(int))
            {
                var countType = IsNullableType(right.Type) ? typeof(int?) : typeof(int);
                if (TryConvertExpression(right, countType) is not { } count)
                    return ToError(node.Right, "Shift count must be convertible to int.");

                right = count;
            }

            return Expression.MakeBinary(expressionType, left, right);
        }

        // Ensure types
        if (!EnsureTheSameTypes(node, ref left, ref right))
            return null;

        // Mixed-type user-defined operator (e.g. TimeSpan * int -> op_Multiply(TimeSpan, double))
        if (left.Type != right.Type && TryResolveUserDefinedBinaryOperator(expressionType, left, right) is { } userOperator)
            return userOperator;

        if (_checkedContext)
            expressionType = expressionType switch
            {
                ExpressionType.Add => ExpressionType.AddChecked,
                ExpressionType.Subtract => ExpressionType.SubtractChecked,
                ExpressionType.Multiply => ExpressionType.MultiplyChecked,
                _ => expressionType
            };

        return Expression.MakeBinary(expressionType, left, right);
    }
    public override Expression? VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        var operand = Visit(node.Operand);
        if (operand == null)
            return null;

        var expressionType = (SyntaxKind)node.OperatorToken.RawKind switch
        {
            SyntaxKind.ExclamationToken => ExpressionType.Not,
            SyntaxKind.TildeToken => ExpressionType.OnesComplement,
            SyntaxKind.PlusToken => ExpressionType.UnaryPlus,
            SyntaxKind.MinusToken => ExpressionType.Negate,

            _ => ExpressionType.Throw
        };
        if (expressionType == ExpressionType.Throw)
            return ToError(node, $"Unsupported unary operator '{node.OperatorToken.ValueText}'.");

        // Operator ! only for bool
        if (expressionType == ExpressionType.Not
            && (Nullable.GetUnderlyingType(operand.Type) ?? operand.Type) is { } notType && notType != typeof(bool)
            && (notType.IsPrimitive || notType.IsEnum || notType == typeof(decimal)))
        {
            return ToError(node, $"Operator '!' cannot be applied to operand of type '{operand.Type.GetFriendlyTypeName()}'.");
        }

        // Fold so a negative literal stays a constant (C# constant conversion, e.g. new sbyte[] { -5 })
        if (expressionType == ExpressionType.Negate && operand is ConstantExpression { Value: int constant })
            return Expression.Constant(-constant);

        // C# complements an enum on its underlying type and converts back
        if (expressionType == ExpressionType.OnesComplement && (Nullable.GetUnderlyingType(operand.Type) ?? operand.Type) is { IsEnum: true } enumType)
        {
            var underlyingType = Enum.GetUnderlyingType(enumType);
            var operandType = operand.Type != enumType ? typeof(Nullable<>).MakeGenericType(underlyingType) : underlyingType;
            var complement = Expression.OnesComplement(PromoteSmallInteger(Expression.Convert(operand, operandType)));
            return Expression.Convert(complement, operand.Type);
        }

        // C# promotes operands smaller than int (byte/sbyte/short/ushort/char) to int
        if (expressionType is ExpressionType.OnesComplement or ExpressionType.Negate or ExpressionType.UnaryPlus)
            operand = PromoteSmallInteger(operand);

        // C# has no unary minus for uint - the operand converts to long
        if (expressionType == ExpressionType.Negate && (Nullable.GetUnderlyingType(operand.Type) ?? operand.Type) == typeof(uint))
            operand = ToCast(operand, IsNullableType(operand.Type) ? typeof(long?) : typeof(long));

        if (_checkedContext && expressionType == ExpressionType.Negate)
            expressionType = ExpressionType.NegateChecked;

        return Expression.MakeUnary(expressionType, operand, null);
    }
    public override Expression? VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        if ((SyntaxKind)node.OperatorToken.RawKind == SyntaxKind.ExclamationToken)
            return Visit(node.Operand);

        return ToError(node, $"Unsupported unary operator '{node.OperatorToken.ValueText}'.");
    }

    public override Expression? VisitCheckedExpression(CheckedExpressionSyntax node)
    {
        var previous = _checkedContext;
        _checkedContext = (SyntaxKind)node.Keyword.RawKind == SyntaxKind.CheckedKeyword;

        try
        {
            return Visit(node.Expression);
        }
        finally
        {
            _checkedContext = previous;
        }
    }
    public override Expression? VisitSizeOfExpression(SizeOfExpressionSyntax node)
    {
        var type = ResolveType(node.Type);
        if (type == null)
            return null;

        var size = Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean or TypeCode.SByte or TypeCode.Byte => 1,
            TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Char => 2,
            TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Single => 4,
            TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Double => 8,
            TypeCode.Decimal => 16,
            _ => 0
        };

        return size > 0
            ? Expression.Constant(size)
            : ToError(node, $"Operator sizeof is not supported for type '{type.GetFriendlyTypeName()}'.");
    }
    public override Expression? VisitThrowExpression(ThrowExpressionSyntax node)
    {
        if (!_options.AllowThrowExpressions)
            return ToError(node, "Throw expressions are not allowed.");

        var exception = Visit(node.Expression);
        if (exception == null)
            return null;

        if (IsNullLiteral(exception))
            exception = Expression.Constant(null, typeof(Exception));
        else if (!typeof(Exception).IsAssignableFrom(exception.Type))
            return ToError(node, "The thrown value must be an exception.");

        return new DelayThrowExpression(exception);
    }

    private Expression? BuildEnumBinaryOperation(SyntaxNode node, ExpressionType expressionType, Expression left, Expression right)
    {
        // Nullable enum operand
        var leftType = Nullable.GetUnderlyingType(left.Type) ?? left.Type;
        var rightType = Nullable.GetUnderlyingType(right.Type) ?? right.Type;

        if ((leftType != left.Type && leftType.IsEnum) || (rightType != right.Type && rightType.IsEnum))
        {
            var enumType = leftType.IsEnum ? leftType : rightType;
            var operandType = typeof(Nullable<>).MakeGenericType(Enum.GetUnderlyingType(enumType));
            var bothEnums = leftType.IsEnum && rightType.IsEnum;
            var numberSide = leftType.IsEnum ? right : left;

            if ((bothEnums && leftType != rightType)
                || Lift(left) is not { } l
                || Lift(right) is not { } r)
            {
                return ToError(node, $"Operator cannot be applied to operands of type '{left.Type.GetFriendlyTypeName()}' and '{right.Type.GetFriendlyTypeName()}'.");
            }

            var nullableEnumType = typeof(Nullable<>).MakeGenericType(enumType);

            switch (expressionType)
            {
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                    if (bothEnums || IsNullLiteral(numberSide) || numberSide is ConstantExpression { Value: 0 })
                        return Expression.MakeBinary(expressionType, l, r);
                    break;

                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                    if (bothEnums)
                        return Expression.MakeBinary(expressionType, l, r);
                    break;

                case ExpressionType.Subtract:
                    return bothEnums
                        ? Expression.MakeBinary(expressionType, l, r)
                        : Expression.Convert(Expression.MakeBinary(expressionType, l, r), nullableEnumType);

                // Can not add when both are enum types (CS0019)
                case ExpressionType.Add:
                    if (!bothEnums)
                        return Expression.Convert(Expression.MakeBinary(expressionType, l, r), nullableEnumType);
                    break;

                case ExpressionType.And:
                case ExpressionType.Or:
                case ExpressionType.ExclusiveOr:
                    if (bothEnums)
                        return Expression.Convert(Expression.MakeBinary(expressionType, l, r), nullableEnumType);
                    break;
            }

            return ToError(node, $"Operator cannot be applied to operands of type '{left.Type.GetFriendlyTypeName()}' and '{right.Type.GetFriendlyTypeName()}'.");

            Expression? Lift(Expression e) => (Nullable.GetUnderlyingType(e.Type) ?? e.Type).IsEnum
                ? Expression.Convert(e, operandType)
                : TryConvertExpression(e, operandType);
        }

        var leftIsEnum = left.Type.IsEnum;
        var rightIsEnum = right.Type.IsEnum;

        if (leftIsEnum && rightIsEnum)
        {
            if (left.Type != right.Type)
                return ToError(node, $"Operator cannot be applied to operands of enum types '{left.Type.GetFriendlyTypeName()}' and '{right.Type.GetFriendlyTypeName()}'.");

            // Linq.Expressions doesn't implement any operator for the enum type itself.
            var enumType = left.Type;
            var underlyingType = Enum.GetUnderlyingType(enumType);

            // ReSharper disable once ConvertSwitchStatementToSwitchExpression
            switch (expressionType)
            {
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                    return Expression.MakeBinary(expressionType, left, right);

                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                    return Expression.MakeBinary(expressionType, Expression.Convert(left, underlyingType), Expression.Convert(right, underlyingType));

                case ExpressionType.Subtract:
                    return Expression.Subtract(Expression.Convert(left, underlyingType), Expression.Convert(right, underlyingType));

                case ExpressionType.And:
                case ExpressionType.Or:
                case ExpressionType.ExclusiveOr:
                    return Expression.Convert(Expression.MakeBinary(expressionType, Expression.Convert(left, underlyingType), Expression.Convert(right, underlyingType)), enumType);

                default:
                    return ToError(node, $"Operator is not defined for the enum type '{enumType.GetFriendlyTypeName()}'.");
            }
        }

        // Exactly one side is an enum; the other side must be its underlying numeric type (or - only for ==/!= - the literal 0)
        var enumSide = leftIsEnum ? left : right;
        var otherSide = leftIsEnum ? right : left;
        var enumType2 = enumSide.Type;
        var underlyingType2 = Enum.GetUnderlyingType(enumType2);

        if (expressionType is ExpressionType.Equal or ExpressionType.NotEqual && otherSide is ConstantExpression { Value: 0 })
            return Expression.MakeBinary(expressionType, enumSide, Expression.Convert(otherSide, enumType2));

        if (expressionType == ExpressionType.Add && TryConvertExpression(otherSide, underlyingType2) is { } addOperand)
            return Expression.Convert(Expression.Add(Expression.Convert(enumSide, underlyingType2), addOperand), enumType2);

        if (expressionType == ExpressionType.Subtract && TryConvertExpression(otherSide, underlyingType2) is { } subtractOperand)
        {
            var enumAsUnderlying = Expression.Convert(enumSide, underlyingType2);
            var difference = leftIsEnum
                ? Expression.Subtract(enumAsUnderlying, subtractOperand)
                : Expression.Subtract(subtractOperand, enumAsUnderlying);

            return Expression.Convert(difference, enumType2);
        }

        return ToError(node, $"Operator cannot be applied to operands of type '{left.Type.GetFriendlyTypeName()}' and '{right.Type.GetFriendlyTypeName()}'.");
    }
    private Expression? TryResolveUserDefinedBinaryOperator(ExpressionType type, Expression left, Expression right)
    {
        var operatorName = type switch
        {
            ExpressionType.Add => "op_Addition",
            ExpressionType.Subtract => "op_Subtraction",
            ExpressionType.Multiply => "op_Multiply",
            ExpressionType.Divide => "op_Division",
            ExpressionType.Modulo => "op_Modulus",
            _ => null
        };
        if (operatorName == null)
            return null;

        if (left.Type == right.Type)
        {
            if (MakeBinary(left.Type) is { } makeBinary)
                return makeBinary;
        }
        else
        {
            if (MakeBinary(left.Type) is { } makeBinary)
                return makeBinary;

            if (MakeBinary(right.Type) is { } makeBinary2)
                return makeBinary2;
        }

        return null;

        Expression? MakeBinary(Type declaringType)
        {
            if (declaringType.IsPrimitive)
                return null;

            var methods = GetMethods(declaringType, operatorName, BindingFlags.Static);

            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < methods.Count; i++)
            {
                var method = methods[i];

                var parameters = method.GetParameters();
                if (parameters.Length != 2)
                    continue;

                if (TryConvertExpression(left, parameters[0].ParameterType) is { } convertedLeft
                    && TryConvertExpression(right, parameters[1].ParameterType) is { } convertedRight)
                {
                    return Expression.MakeBinary(type, convertedLeft, convertedRight, false, method);
                }
            }

            return null;
        }
    }

    private bool EnsureTheSameTypes(SyntaxNode node, ref Expression e1, ref Expression e2, bool bestCommonOnly = false)
    {
        // throw operand takes the type of the other side, e.g. cond ? value : throw ex
        if (e1 is DelayThrowExpression || e2 is DelayThrowExpression)
        {
            if (e1 is DelayThrowExpression && e2 is DelayThrowExpression)
            {
                ToError(node, "Cannot infer the type of 'throw' when both sides throw.");
                return false;
            }

            if (e1 is DelayThrowExpression dt1)
                e1 = Expression.Throw(dt1.Exception, e2.Type);
            else if (e2 is DelayThrowExpression dt2)
                e2 = Expression.Throw(dt2.Exception, e1.Type);

            return true;
        }

        // new() operand
        if (e1 is DelayNewExpression || e2 is DelayNewExpression)
        {
            if (e1 is DelayNewExpression && e2 is DelayNewExpression)
            {
                ToError(node, "Cannot infer type for 'new()' here.");
                return false;
            }

            if (e1 is DelayNewExpression dn1)
            {
                if (ResolveDelayNew(dn1, e2.Type) is not { } created1)
                    return false;
                e1 = created1;
            }
            else if (e2 is DelayNewExpression dn2)
            {
                if (ResolveDelayNew(dn2, e1.Type) is not { } created2)
                    return false;
                e2 = created2;
            }

            return true;
        }

        // Default
        if (e1 is DelayDefaultExpression || e2 is DelayDefaultExpression)
        {
            switch (e1)
            {
                case DelayDefaultExpression when e2 is DelayDefaultExpression:
                    ToError(node, "Cannot infer the type of 'default' when both sides are 'default'.");
                    return false;
                case DelayDefaultExpression:
                    e1 = Expression.Default(e2.Type);
                    break;
                default:
                    e2 = Expression.Default(e1.Type);
                    break;
            }

            return true;
        }

        // Null mismatch
        if (e1 is ConstantExpression c && c.Type == typeof(object) && c.Value == null)
        {
            var type = e2.Type;
            if (type.IsValueType && !IsNullableType(type))
                type = typeof(Nullable<>).MakeGenericType(type);
            e1 = Expression.Convert(e1, type);
        }
        else if (e2 is ConstantExpression c2 && c2.Type == typeof(object) && c2.Value == null)
        {
            var type = e1.Type;
            if (type.IsValueType && !IsNullableType(type))
                type = typeof(Nullable<>).MakeGenericType(type);
            e2 = Expression.Convert(e2, type);
        }

        // Nullable mismatch
        var t1 = e1.Type;
        var t2 = e2.Type;

        if (t1 == t2)
            return true;

        // Operator ?: and switch require one of the operand types, so lifting to a third type is a binary-operator rule only
        if (!bestCommonOnly)
        {
            if (IsNullableType(t1))
            {
                if (!IsNullableType(t2) && t2.IsValueType)
                    e2 = Expression.Convert(e2, typeof(Nullable<>).MakeGenericType(t2));
            }
            else if (IsNullableType(t2) && t1.IsValueType)
                e1 = Expression.Convert(e1, typeof(Nullable<>).MakeGenericType(t1));
        }

        t1 = e1.Type;
        t2 = e2.Type;

        if (t1 == t2)
            return true;

        // Implicit conversion
        if (TryConvertExpression(e2, t1) is { } converted2)
        {
            e2 = converted2;
            return true;
        }

        if (TryConvertExpression(e1, t2) is { } converted1)
        {
            e1 = converted1;
            return true;
        }

        // C# constant conversion, e.g. the int literal in '5ul + 5' or '3u - 1' takes the unsigned type
        if (TryConvertConstant(e2, t1) is { } constant2)
        {
            e2 = constant2;
            return true;
        }

        if (TryConvertConstant(e1, t2) is { } constant1)
        {
            e1 = constant1;
            return true;
        }

        if (bestCommonOnly && (t1.IsValueType || t2.IsValueType))
        {
            ToError(node, $"No implicit conversion between '{t1.GetFriendlyTypeName()}' and '{t2.GetFriendlyTypeName()}'.");
            return false;
        }

        // uint mixed with a signed sbyte/short/int promotes both operands to long
        var u1 = Nullable.GetUnderlyingType(t1) ?? t1;
        var u2 = Nullable.GetUnderlyingType(t2) ?? t2;

        if ((u1 == typeof(uint) && (u2 == typeof(int) || u2 == typeof(short) || u2 == typeof(sbyte)))
            || (u2 == typeof(uint) && (u1 == typeof(int) || u1 == typeof(short) || u1 == typeof(sbyte))))
        {
            var longType = u1 != t1 ? typeof(long?) : typeof(long);
            e1 = Expression.Convert(e1, longType);
            e2 = Expression.Convert(e2, longType);
            return true;
        }

        // Two distinct numeric types with no implicit conversion between them (e.g. decimal vs double) can never be resolved.
        if (TypeUtils.IsNumericType(t1) && TypeUtils.IsNumericType(t2))
        {
            ToError(node, $"Can not apply operator to '{t1.GetFriendlyTypeName()}' and '{t2.GetFriendlyTypeName()}' types.");
            return false;
        }

        // Any other mismatch is left to Expression.MakeBinary/Expression.Condition.
        // They may still succeed via a mixed-type operator overload (e.g. DateTime - TimeSpan) or throw error otherwise.
        return true;
    }
    private static Expression PromoteSmallInteger(Expression expression)
    {
        var underlying = Nullable.GetUnderlyingType(expression.Type) ?? expression.Type;
        if (underlying.IsEnum)
            return expression;

        return Type.GetTypeCode(underlying) switch
        {
            TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Char => ToCast(expression, IsNullableType(expression.Type) ? typeof(int?) : typeof(int)),
            _ => expression
        };
    }
}
