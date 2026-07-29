using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TagBites.Utils;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    public override Expression? VisitTupleExpression(TupleExpressionSyntax node)
    {
        var args = ResolveParameters(node.Arguments);
        if (args == null)
            return null;

        var result = BuildValueTuple(args);
        if (result == null)
            return ToError(node);

        // ValueTuple with defined or inferred element names
        string?[]? names = null;
        ValueTupleShape?[]? argShapes = null;

        for (var i = 0; i < node.Arguments.Count; i++)
        {
            var argument = node.Arguments[i];

            // Explicit name
            var elementName = argument.NameColon?.Name.Identifier.Text;
            if (elementName != null)
            {
                switch (ValueTupleShape.GetReservedElementPosition(elementName))
                {
                    case 0:
                        return ToError(argument, $"Tuple element name '{elementName}' is disallowed at any position.");
                    case var p and > 0 when p != i + 1:
                        return ToError(argument, $"Tuple element name '{elementName}' is only allowed at position {p}.");
                }
            }

            // Implicit name, e.g. (n, a.B) -> n, B; but not reserved name and not duplicated
            else if (ValueTupleShape.GetImplicitElementName(argument.Expression) is { } inferred && !IsDuplicateElementName(i, inferred))
                elementName = inferred;

            if (elementName != null)
            {
                names ??= new string?[node.Arguments.Count];
                names[i] = elementName;
            }

            var childShape = GetTupleShape(args[i]!);
            if (childShape != null)
            {
                argShapes ??= new ValueTupleShape?[node.Arguments.Count];
                argShapes[i] = childShape;
            }
        }

        if (names != null || argShapes != null)
            SetTupleShape(result, new ValueTupleShape { Names = names, Args = argShapes });

        return result;

        bool IsDuplicateElementName(int index, string name)
        {
            for (var i = node.Arguments.Count - 1; i >= 0; i--)
                if (i != index)
                {
                    var argument = node.Arguments[i];
                    var other = argument.NameColon?.Name.Identifier.Text
                                ?? ValueTupleShape.GetImplicitElementName(argument.Expression);

                    if (other != null && string.Equals(other, name, _nameComparison))
                        return true;
                }

            return false;
        }
    }

    private static Expression? BuildValueTuple(IList<Expression> elements)
    {
        var count = elements.Count;
        if (count == 0)
            return null;

        if (count <= 7)
        {
            s_valueTupleCreate ??= BuildValueTupleCreateTable();
            var create = s_valueTupleCreate[count];
            if (create == null)
                return null;

            var types = new Type[count];
            for (var i = 0; i < count; i++)
                types[i] = elements[i].Type;

            return Expression.Call(null, create.MakeGenericMethod(types), elements);
        }

        // ValueTuple<T1..T7, TRest> with the remaining elements nested in Rest
        var rest = BuildValueTuple(elements.Skip(7).ToList());
        if (rest == null)
            return null;

        var typeArguments = new Type[8];
        var constructorArguments = new Expression[8];

        for (var i = 0; i < 7; i++)
        {
            typeArguments[i] = elements[i].Type;
            constructorArguments[i] = elements[i];
        }

        typeArguments[7] = rest.Type;
        constructorArguments[7] = rest;

        var tupleType = typeof(ValueTuple<,,,,,,,>).MakeGenericType(typeArguments);
        return Expression.New(tupleType.GetConstructors()[0], constructorArguments);
    }
    private static MethodInfo?[] BuildValueTupleCreateTable()
    {
        var table = new MethodInfo?[8];

        foreach (var method in typeof(ValueTuple).GetMethods(BindingFlags.Public | BindingFlags.Static))
            if (method.Name == nameof(ValueTuple.Create) && method.GetParameters().Length is var count and >= 1 and <= 7)
                table[count] = method;

        return table;
    }
    private static Expression? BuildTupleElementAccess(Expression tuple, int index)
    {
        while (index >= 7)
        {
            var rest = tuple.Type.GetField("Rest");
            if (rest == null)
                return null;

            tuple = Expression.Field(tuple, rest);
            index -= 7;
        }

        var field = tuple.Type.GetField($"Item{index + 1}");
        return field == null ? null : Expression.Field(tuple, field);
    }
    private static Expression[]? GetTupleItemAccessors(Expression expression, int count)
    {
        var type = expression.Type;
        if (!IsValueTupleType(type) || type.GetGenericArguments().Length != count)
            return null;

        var result = new Expression[count];
        for (var i = 0; i < count; i++)
            result[i] = Expression.MakeMemberAccess(expression, (MemberInfo?)type.GetField($"Item{i + 1}") ?? type.GetProperty($"Item{i + 1}")!);

        return result;
    }

    private Expression? BuildTupleEquality(SyntaxNode node, Expression left, Expression right)
    {
        var count = left.Type.GetGenericArguments().Length;
        if (count != right.Type.GetGenericArguments().Length)
            return ToError(node, $"Operator cannot be applied to operands of type '{left.Type.GetFriendlyTypeName()}' and '{right.Type.GetFriendlyTypeName()}'.");

        Expression? result = null;

        for (var i = 0; i < count; i++)
        {
            var name = i == 7 ? "Rest" : $"Item{i + 1}";
            Expression leftItem = Expression.Field(left, name);
            Expression rightItem = Expression.Field(right, name);

            Expression? comparison;
            if (IsValueTupleType(leftItem.Type) && IsValueTupleType(rightItem.Type))
                comparison = BuildTupleEquality(node, leftItem, rightItem);
            else if (EnsureTheSameTypes(node, ref leftItem, ref rightItem) && leftItem.Type == rightItem.Type)
                comparison = Expression.Equal(leftItem, rightItem);
            else
                return ToError(node, $"Operator cannot be applied to operands of type '{left.Type.GetFriendlyTypeName()}' and '{right.Type.GetFriendlyTypeName()}'.");

            if (comparison == null)
                return null;

            result = result == null ? comparison : Expression.AndAlso(result, comparison);
        }

        return result;
    }
    private static int? TryGetTupleItemIndex(string name)
    {
        return name.Length > 4 && name.StartsWith("Item", StringComparison.Ordinal) && int.TryParse(name.Substring(4), out var n) && n > 0
            ? n - 1
            : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTupleShape? GetTupleShape(Expression expression)
    {
        return _tupleShapes != null && _tupleShapes.TryGetValue(expression, out var shape) ? shape : null;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetTupleShape(Expression expression, ValueTupleShape? shape)
    {
        if (shape == null)
            return;

        _tupleShapes ??= new Dictionary<Expression, ValueTupleShape>();
        _tupleShapes[expression] = shape;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetSequenceElementShape(Expression sequence, ValueTupleShape? elementShape)
    {
        // A sequence carries its element shape nested under Args[0] (IEnumerable<T> -> [T]),
        // the same layout ComputeCallResultShape produces for LINQ results.
        if (elementShape != null)
            SetTupleShape(sequence, new ValueTupleShape { Args = [elementShape] });
    }
}
