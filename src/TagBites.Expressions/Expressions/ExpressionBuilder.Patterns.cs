using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TagBites.Utils;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    public override Expression? VisitIsPatternExpression(IsPatternExpressionSyntax node)
    {
        var left = Visit(node.Expression);
        if (left == null)
            return null;

        return ResolvePattern(left, node.Pattern);
    }

    private Expression? ResolvePattern(Expression expression, PatternSyntax pattern)
    {
        var expressionType = expression.Type;

        switch (pattern)
        {
            // is null, is not null, is {}
            case ConstantPatternSyntax { Expression: LiteralExpressionSyntax { Token.Text: "null" } }:
            case UnaryPatternSyntax { OperatorToken.Text: "not", Pattern: ConstantPatternSyntax { Expression: LiteralExpressionSyntax { Token.Text: "null" } } }:
            case RecursivePatternSyntax { Designation: null, PositionalPatternClause: null, Type: null, PropertyPatternClause.Subpatterns.Count: 0 }:
                {
                    var isNullCheck = pattern is ConstantPatternSyntax;

                    if (IsNullableType(expressionType))
                    {
                        var hasValue = Expression.MakeMemberAccess(expression, expressionType.GetProperty(nameof(Nullable<>.HasValue))!);
                        return isNullCheck ? Expression.Not(hasValue) : hasValue;
                    }

                    if (expressionType.IsValueType)
                        return ToError(pattern, $"Cannot convert null to '{expressionType.Name}'.");

                    var isNull = Expression.ReferenceEqual(expression, Expression.Constant(null, expressionType));
                    return isNullCheck ? isNull : Expression.Not(isNull);
                }

            // is const
            case ConstantPatternSyntax p:
                {
                    var right = Visit(p.Expression);
                    if (right == null)
                        return null;

                    // Type name is a pattern, not a constant
                    if (TryGetPatternType(right) is { } patternType)
                        return ToTypePattern(pattern, expression, patternType);

                    if (IsPatternConstantMismatch(expressionType, right))
                        return ToError(pattern, $"Pattern constant of type '{right.Type.GetFriendlyTypeName()}' does not convert to the input type '{expressionType.GetFriendlyTypeName()}'.");

                    // A value-type constant against an object input tests the runtime type first, e.g. (object)5 is 5
                    if (expressionType == typeof(object) && right.Type.IsValueType)
                        return Expression.AndAlso(
                            ToIsOperator(expression, Expression.Constant(right.Type)),
                            Expression.MakeBinary(ExpressionType.Equal, Expression.Convert(expression, right.Type), right));

                    if (!EnsureTheSameTypes(pattern, ref expression, ref right))
                        return null;

                    return Expression.MakeBinary(ExpressionType.Equal, expression, right);
                }

            // is type
            case TypePatternSyntax p:
                {
                    var type = ResolveType(p.Type);
                    if (type == null)
                        return null;

                    return ToTypePattern(pattern, expression, type);
                }

            // or, and
            case BinaryPatternSyntax p:
                {
                    var left = ResolvePattern(expression, p.Left);
                    if (left == null)
                        return null;

                    // The right side of 'and' sees the type narrowed by the left, e.g. (object)x is int and > 5
                    var isAnd = (SyntaxKind)p.OperatorToken.RawKind == SyntaxKind.AndKeyword;
                    if (isAnd && GetPatternNarrowingType(p.Left) is { } narrowingType)
                    {
                        // Narrows only when it resolves to a type, not a constant pattern
                        var narrowedType = narrowingType switch
                        {
                            IdentifierNameSyntax bareName => TryResolveTypeByName(bareName.Identifier.Text),
                            MemberAccessExpressionSyntax qualifiedName => TryResolveNamespaceQualifiedType(qualifiedName),
                            TypeSyntax typeName => ResolveType(typeName),
                            _ => null
                        };

                        if (narrowedType != null && narrowedType != expression.Type)
                            expression = Expression.Convert(expression, narrowedType);
                    }

                    var right = ResolvePattern(expression, p.Right);
                    if (right == null)
                        return null;

                    return isAnd
                        ? Expression.AndAlso(left, right)
                        : (SyntaxKind)p.OperatorToken.RawKind == SyntaxKind.OrKeyword
                            ? Expression.OrElse(left, right)
                            : ToError(pattern);
                }

            // >, <, >=, <=
            case RelationalPatternSyntax p:
                {
                    var right = Visit(p.Expression);
                    if (right == null)
                        return null;

                    if (IsPatternConstantMismatch(expressionType, right))
                        return ToError(pattern, $"Pattern constant of type '{right.Type.GetFriendlyTypeName()}' does not convert to the input type '{expressionType.GetFriendlyTypeName()}'.");

                    var opr = (SyntaxKind)p.OperatorToken.RawKind switch
                    {
                        SyntaxKind.GreaterThanToken => ExpressionType.GreaterThan,
                        SyntaxKind.GreaterThanEqualsToken => ExpressionType.GreaterThanOrEqual,
                        SyntaxKind.LessThanToken => ExpressionType.LessThan,
                        SyntaxKind.LessThanEqualsToken => ExpressionType.LessThanOrEqual,
                        _ => (ExpressionType?)null
                    };
                    if (!opr.HasValue)
                        return ToError(pattern);

                    // A value-type constant against an object input tests the runtime type first, e.g. (object)5 is > 3
                    if (expressionType == typeof(object) && right.Type.IsValueType)
                        return Expression.AndAlso(
                            ToIsOperator(expression, Expression.Constant(right.Type)),
                            Expression.MakeBinary(opr.Value, Expression.Convert(expression, right.Type), right));

                    if (!EnsureTheSameTypes(pattern, ref expression, ref right))
                        return null;

                    // Enums have no relational operator of their own
                    return expression.Type.IsEnum || right.Type.IsEnum
                        ? BuildEnumBinaryOperation(pattern, opr.Value, expression, right)
                        : Expression.MakeBinary(opr.Value, expression, right);
                }

            // not
            case UnaryPatternSyntax p:
                {
                    var right = ResolvePattern(expression, p.Pattern);
                    if (right == null)
                        return null;

                    return (SyntaxKind)p.OperatorToken.RawKind switch
                    {
                        SyntaxKind.NotKeyword => Expression.Not(right),
                        _ => ToError(p)
                    };
                }

            // is ()
            case ParenthesizedPatternSyntax p:
                {
                    // ReSharper disable once TailRecursiveCall
                    return ResolvePattern(expression, p.Pattern);
                }

            // is var x
            case VarPatternSyntax { Designation: SingleVariableDesignationSyntax v }:
                {
                    var name = v.Identifier.Text;
                    var declareExpression = DeclareVariable(v, expression, name);
                    if (declareExpression == null)
                        return null;

                    return Expression.Block(declareExpression, Expression.Constant(true));
                }

            // is var (a, b)
            case VarPatternSyntax { Designation: ParenthesizedVariableDesignationSyntax d }:
                return ResolveVarDesignation(expression, d);

            // is ... x
            case DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax v } p:
                {
                    var type = ResolveType(p.Type);
                    if (type == null)
                        return null;

                    var typeCheck = ToTypePattern(pattern, expression, type);
                    if (typeCheck == null)
                        return null;

                    var name = v.Identifier.Text;
                    var declareExpression = DeclareVariable(v, Expression.Convert(expression, type), name);
                    if (declareExpression == null)
                        return null;

                    return Expression.AndAlso(
                        typeCheck,
                        Expression.Block(declareExpression, Expression.Constant(true)));
                }

            // is { } x, is (a, b) x
            case RecursivePatternSyntax p:
                {
                    Expression checkExpression;

                    if (!IsNullableType(expressionType))
                        checkExpression = ToIsNotNull(expression);
                    else
                    {
                        checkExpression = Expression.MakeMemberAccess(expression, expressionType.GetProperty(nameof(Nullable<>.HasValue))!);
                        expression = Expression.MakeMemberAccess(expression, expressionType.GetProperty(nameof(Nullable<>.Value))!);
                    }

                    // Type
                    if (p.Type != null)
                    {
                        var customType = ResolveType(p.Type);
                        if (customType == null)
                            return null;

                        checkExpression = ToIsOperator(expression, Expression.Constant(customType));
                        expression = ToCast(expression, customType);
                    }

                    // Positional
                    var deconstructVariables = Array.Empty<ParameterExpression>();
                    if (p.PositionalPatternClause != null)
                    {
                        var subpatterns = p.PositionalPatternClause.Subpatterns;
                        var elements = GetTupleItemAccessors(expression, subpatterns.Count);

                        if (elements == null)
                        {
                            var deconstruct = GetDeconstructMethod(expression.Type, subpatterns.Count);
                            if (deconstruct == null)
                                return ToError(p.PositionalPatternClause, $"No Deconstruct method for '{expression.Type.GetFriendlyTypeName()}' with {subpatterns.Count} parameters.");

                            deconstructVariables = deconstruct.GetParameters().ToFastArray(x => Expression.Variable(x.ParameterType.GetElementType()!));
                            elements = deconstructVariables.ToFastArray(x => (Expression)x);
                            checkExpression = Expression.Block(Expression.Call(expression, deconstruct, elements), checkExpression);
                        }

                        for (var i = 0; i < subpatterns.Count; i++)
                        {
                            var condition = ResolvePattern(elements[i], subpatterns[i].Pattern);
                            if (condition == null)
                                return null;

                            checkExpression = Expression.AndAlso(checkExpression, condition);
                        }
                    }

                    // Properties
                    if (p.PropertyPatternClause?.Subpatterns.Count > 0)
                    {
                        foreach (var property in p.PropertyPatternClause.Subpatterns)
                        {
                            // Extended property pattern (A.B.C:) walks the member path with null checks on the intermediate steps
                            Expression? guard = null;
                            var propertyValueExpression = expression;

                            foreach (var propertyName in GetPropertyPatternPath(property.ExpressionColon?.Expression))
                            {
                                if (propertyName == null)
                                    return ToError(property);

                                if (!ReferenceEquals(propertyValueExpression, expression) && (!propertyValueExpression.Type.IsValueType || IsNullableType(propertyValueExpression.Type)))
                                {
                                    var notNull = ToIsNotNull(propertyValueExpression);
                                    guard = guard == null ? notNull : Expression.AndAlso(guard, notNull);
                                }

                                propertyValueExpression = ResolveCustomMember(propertyValueExpression, propertyName)
                                                          ?? ResolveMember(property, propertyValueExpression, propertyName);
                                if (propertyValueExpression == null)
                                    return null;
                            }

                            var condition = ResolvePattern(propertyValueExpression, property.Pattern);
                            if (condition == null)
                                return null;

                            if (guard != null)
                                condition = Expression.AndAlso(guard, condition);

                            checkExpression = Expression.AndAlso(checkExpression, condition);
                        }
                    }

                    // Variable
                    if (p.Designation != null && p.Designation is not DiscardDesignationSyntax)
                    {
                        if (p.Designation is not SingleVariableDesignationSyntax v)
                            return ToError(p.Designation);

                        var name = v.Identifier.Text;
                        var declareExpression = DeclareVariable(v, Expression.Convert(expression, expression.Type), name);
                        if (declareExpression == null)
                            return null;

                        checkExpression = Expression.AndAlso(checkExpression, Expression.Block(declareExpression, Expression.Constant(true)));
                    }

                    return deconstructVariables.Length > 0 ? Expression.Block(deconstructVariables, checkExpression) : checkExpression;
                }

            // is ... _
            case DiscardPatternSyntax:
                return Expression.Constant(true);

            // is [1, 2, 3], is [1, .., 3]
            case ListPatternSyntax p:
                {
                    Expression checkExpression;

                    if (!IsNullableType(expressionType))
                        checkExpression = ToIsNotNull(expression);
                    else
                    {
                        checkExpression = Expression.MakeMemberAccess(expression, expressionType.GetProperty(nameof(Nullable<>.HasValue))!);
                        expression = Expression.MakeMemberAccess(expression, expressionType.GetProperty(nameof(Nullable<>.Value))!);
                    }

                    var receiver = Expression.Variable(expression.Type, "receiver");

                    Expression length;
                    if (expression.Type.IsArray && expression.Type.GetArrayRank() == 1)
                        length = Expression.ArrayLength(receiver);
                    else
                    {
                        var lengthProperty = GetCountProperty(expression.Type, "Length") ?? GetCountProperty(expression.Type, "Count");
                        if (lengthProperty == null)
                            return ToError(pattern, $"List pattern requires a 'Length' or 'Count' property on type '{expression.Type.GetFriendlyTypeName()}'.");

                        length = Expression.MakeMemberAccess(receiver, lengthProperty);
                    }

                    var subpatterns = p.Patterns;
                    var sliceIndex = -1;
                    for (var i = 0; i < subpatterns.Count; i++)
                        if (subpatterns[i] is SlicePatternSyntax)
                        {
                            sliceIndex = i;
                            break;
                        }

                    if (sliceIndex < 0)
                    {
                        checkExpression = Expression.AndAlso(checkExpression, Expression.Equal(length, Expression.Constant(subpatterns.Count)));

                        for (var i = 0; i < subpatterns.Count; i++)
                        {
                            var itemAccess = ResolveItemCall(pattern, receiver, [Expression.Constant(i)]);
                            if (itemAccess == null)
                                return null;

                            var condition = ResolvePattern(itemAccess, subpatterns[i]);
                            if (condition == null)
                                return null;

                            checkExpression = Expression.AndAlso(checkExpression, condition);
                        }
                    }
                    else
                    {
                        var slice = (SlicePatternSyntax)subpatterns[sliceIndex];
                        if (slice.Pattern != null)
                            return ToError(slice, "List pattern slice with a sub-pattern is not supported.");

                        var headCount = sliceIndex;
                        var tailCount = subpatterns.Count - sliceIndex - 1;
                        checkExpression = Expression.AndAlso(checkExpression, Expression.GreaterThanOrEqual(length, Expression.Constant(headCount + tailCount)));

                        for (var i = 0; i < headCount; i++)
                        {
                            var itemAccess = ResolveItemCall(pattern, receiver, [Expression.Constant(i)]);
                            if (itemAccess == null)
                                return null;

                            var condition = ResolvePattern(itemAccess, subpatterns[i]);
                            if (condition == null)
                                return null;

                            checkExpression = Expression.AndAlso(checkExpression, condition);
                        }

                        for (var i = 0; i < tailCount; i++)
                        {
                            var offset = Expression.Subtract(length, Expression.Constant(tailCount - i));
                            var itemAccess = ResolveItemCall(pattern, receiver, [offset]);
                            if (itemAccess == null)
                                return null;

                            var condition = ResolvePattern(itemAccess, subpatterns[sliceIndex + 1 + i]);
                            if (condition == null)
                                return null;

                            checkExpression = Expression.AndAlso(checkExpression, condition);
                        }
                    }

                    if (p.Designation != null && p.Designation is not DiscardDesignationSyntax)
                    {
                        if (p.Designation is not SingleVariableDesignationSyntax v)
                            return ToError(p.Designation);

                        var name = v.Identifier.Text;
                        var declareExpression = DeclareVariable(v, receiver, name);
                        if (declareExpression == null)
                            return null;

                        checkExpression = Expression.AndAlso(checkExpression, Expression.Block(declareExpression, Expression.Constant(true)));
                    }

                    return Expression.Block([receiver], Expression.Assign(receiver, expression), checkExpression);
                }

            // is .. (only valid nested inside a list pattern)
            case SlicePatternSyntax:
                return ToError(pattern, "Slice pattern is only valid inside a list pattern.");
        }

        return ToError(pattern);

        static bool IsPatternConstantMismatch(Type inputType, Expression constant)
        {
            if (IsNullLiteral(constant))
                return false;

            var input = Nullable.GetUnderlyingType(inputType) ?? inputType;
            if (!TypeUtils.IsNumericType(input) && input != typeof(char))
                return false;

            return input != constant.Type
                   && TryConvertExpression(constant, input) == null
                   && TryConvertConstant(constant, input) == null;
        }
    }
    private static Type? TryGetPatternType(Expression expression)
    {
        // A compiler type reference keeps the Type as its value, while typeof(...) has Type == typeof(Type)
        return expression is ConstantExpression { Value: Type type } && expression.Type != typeof(Type)
            ? type
            : null;
    }

    private Expression? ToTypePattern(SyntaxNode node, Expression expression, Type type)
    {
        // C# rejects a type pattern that can never match (unlike the is operator)
        if (!IsPossibleTypeTest(expression.Type, type))
            return ToError(node, $"An expression of type '{expression.Type.GetFriendlyTypeName()}' cannot be handled by a pattern of type '{type.GetFriendlyTypeName()}'.");

        return ToIsOperator(expression, Expression.Constant(type));
    }
    private static bool IsPossibleTypeTest(Type inputType, Type testType)
    {
        var input = Nullable.GetUnderlyingType(inputType) ?? inputType;
        var test = Nullable.GetUnderlyingType(testType) ?? testType;

        if (input == typeof(object) || test == typeof(object) || test.IsAssignableFrom(input) || input.IsAssignableFrom(test))
            return true;

        if (test.IsInterface)
            return input is { IsValueType: false, IsSealed: false };

        return input.IsInterface && test is { IsValueType: false, IsSealed: false };
    }

    private static ExpressionSyntax? GetPatternNarrowingType(PatternSyntax pattern)
    {
        return pattern switch
        {
            TypePatternSyntax p => p.Type,
            DeclarationPatternSyntax p => p.Type,
            ConstantPatternSyntax { Expression: IdentifierNameSyntax or MemberAccessExpressionSyntax } p => p.Expression,
            RecursivePatternSyntax { Type: { } type } => type,
            ParenthesizedPatternSyntax p => GetPatternNarrowingType(p.Pattern),
            BinaryPatternSyntax { OperatorToken.RawKind: (int)SyntaxKind.AndKeyword } p => GetPatternNarrowingType(p.Right) ?? GetPatternNarrowingType(p.Left),
            _ => null
        };
    }

    private Expression? ResolveVarDesignation(Expression expression, ParenthesizedVariableDesignationSyntax designation)
    {
        var count = designation.Variables.Count;
        var elements = GetTupleItemAccessors(expression, count);
        Expression check = Expression.Constant(true);
        var deconstructVariables = Array.Empty<ParameterExpression>();

        if (elements == null)
        {
            var deconstruct = GetDeconstructMethod(expression.Type, count);
            if (deconstruct == null)
                return ToError(designation, $"No Deconstruct method for '{expression.Type.GetFriendlyTypeName()}' with {count} parameters.");

            deconstructVariables = deconstruct.GetParameters().ToFastArray(x => Expression.Variable(x.ParameterType.GetElementType()!));
            elements = deconstructVariables.ToFastArray(x => (Expression)x);
            check = Expression.Block(Expression.Call(expression, deconstruct, elements), check);
        }

        for (var i = 0; i < count; i++)
        {
            var bound = ResolveDesignation(elements[i], designation.Variables[i]);
            if (bound == null)
                return null;

            check = Expression.AndAlso(check, bound);
        }

        return deconstructVariables.Length > 0 ? Expression.Block(deconstructVariables, check) : check;
    }
    private Expression? ResolveDesignation(Expression expression, VariableDesignationSyntax designation)
    {
        switch (designation)
        {
            case DiscardDesignationSyntax:
                return Expression.Constant(true);

            case SingleVariableDesignationSyntax single:
                {
                    var declareExpression = DeclareVariable(single, expression, single.Identifier.Text);
                    return declareExpression == null ? null : Expression.Block(declareExpression, Expression.Constant(true));
                }

            case ParenthesizedVariableDesignationSyntax nested:
                return ResolveVarDesignation(expression, nested);

            default:
                return ToError(designation);
        }
    }

    private static MethodInfo? GetDeconstructMethod(Type type, int count)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "Deconstruct" && m.ReturnType == typeof(void) && m.GetParameters().Length == count && m.GetParameters().All(x => x.IsOut));
    }
    private static IEnumerable<string?> GetPropertyPatternPath(ExpressionSyntax? path)
    {
        switch (path)
        {
            case IdentifierNameSyntax id:
                yield return id.Identifier.Text;
                break;

            case MemberAccessExpressionSyntax { Name: IdentifierNameSyntax name } ma:
                {
                    foreach (var item in GetPropertyPatternPath(ma.Expression))
                        yield return item;
                    yield return name.Identifier.Text;
                    break;
                }

            default:
                yield return null;
                break;
        }
    }
}
