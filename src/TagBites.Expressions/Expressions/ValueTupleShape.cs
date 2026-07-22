using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TagBites.Expressions;

/// <summary>
/// ValueTuple shape, when custom names are used.
/// </summary>
internal sealed class ValueTupleShape
{
    /// <summary>Element names when this position's type is a ValueTuple (null entries for unnamed elements).</summary>
    public string?[]? Names;
    /// <summary>Child shapes positioned by the type's structural children (tuple elements / generic arguments / array element).</summary>
    public ValueTupleShape?[]? Args;


    /// <summary>
    /// Real field (<c>Item1</c>, <c>Item2</c>, ...) and index for a custom element name.
    /// </summary>
    public (string? RealName, int Index) GetRealField(string aliasName, StringComparison nameComparison)
    {
        if (Names == null)
            return default;

        var index = Array.FindIndex(Names, x => string.Equals(x, aliasName, nameComparison));
        return index >= 0
            ? ($"Item{index + 1}", index)
            : default;
    }

    /// <summary>
    /// Implicit element name, e.g. <c>(a, a.B)</c>.
    /// </summary>
    public static string? GetImplicitElementName(ExpressionSyntax expression)
    {
        var name = expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            _ => null
        };

        // Drop reserved name
        return name != null && GetReservedElementPosition(name) < 0
            ? name
            : null;
    }

    /// <summary>
    /// Returns the 1-based position at which a reserved tuple element name is allowed:
    /// <c>0</c> for a reserved member name disallowed everywhere (Rest, ToString, ...),
    /// <c>N</c> for <c>ItemN</c>,
    /// <c>-1</c> when the name is not reserved and may be used at any position.
    /// </summary>
    public static int GetReservedElementPosition(string name)
    {
        // ValueTuple members
        switch (name)
        {
            case "CompareTo":
            case "Deconstruct":
            case "Equals":
            case "GetHashCode":
            case "Rest":
            case "ToString":
                return 0;
        }

        // Not Item1, Item2, ...
        if (name.Length <= 4 || !name.StartsWith("Item", StringComparison.Ordinal) || name[4] == '0')
            return -1;

        var position = 0;

        for (var i = 4; i < name.Length; i++)
        {
            if (!char.IsDigit(name[i]))
                return -1;

            position = position * 10 + (name[i] - '0');
        }

        return position;
    }

    /// <summary>
    /// True when both shapes have the same names and structure.
    /// </summary>
    public static bool NameShapesEqual(ValueTupleShape? a, ValueTupleShape? b, StringComparison nameComparison)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a == null || b == null)
            return false;

        // Names
        if (a.Names == null != (b.Names == null))
            return false;
        if (a.Names != null && b.Names != null)
        {
            if (a.Names.Length != b.Names.Length)
                return false;

            for (var i = a.Names.Length - 1; i >= 0; i--)
                if (!string.Equals(a.Names[i], b.Names[i], nameComparison))
                    return false;
        }

        // Args
        if (a.Args == null != (b.Args == null))
            return false;

        if (a.Args != null && b.Args != null)
        {
            if (a.Args.Length != b.Args.Length)
                return false;

            for (var i = a.Args.Length - 1; i >= 0; i--)
                if (!NameShapesEqual(a.Args[i], b.Args[i], nameComparison))
                    return false;
        }

        return true;
    }

    /// <summary>
    /// Merges two shapes, keeping a name only where both sides agree.
    /// </summary>
    public static ValueTupleShape? MergeShapes(ValueTupleShape? a, ValueTupleShape? b, StringComparison nameComparison)
    {
        // A missing (unnamed) contributor erases names at this position, as C# does on tuple-name conflict.
        if (a == null || b == null)
            return null;

        string?[]? names = null;
        ValueTupleShape?[]? args = null;

        if (a.Names != null && b.Names != null)
        {
            var count = Math.Min(a.Names.Length, b.Names.Length);
            for (var i = 0; i < count; i++)
                if (a.Names[i] != null && string.Equals(a.Names[i], b.Names[i], nameComparison))
                {
                    names ??= new string?[count];
                    names[i] = a.Names[i];
                }
        }

        if (a.Args != null && b.Args != null)
        {
            var count = Math.Min(a.Args.Length, b.Args.Length);
            for (var i = 0; i < count; i++)
                if (MergeShapes(a.Args[i], b.Args[i], nameComparison) is { } merged)
                {
                    args ??= new ValueTupleShape?[count];
                    args[i] = merged;
                }
        }

        return names == null && args == null ? null : new ValueTupleShape { Names = names, Args = args };
    }

    /// <summary>
    /// Result shape of a method call, propagating element names from the argument and instance shapes.
    /// </summary>
    public static ValueTupleShape? ComputeCallResultShape(MethodInfo method, Expression? instance, IList<Expression> arguments, Func<Expression, ValueTupleShape?> getShape, StringComparison nameComparison)
    {
        // Generic method: bind type parameters from the argument shapes, then substitute into the return type.
        if (method.IsGenericMethod)
        {
            var definition = method.GetGenericMethodDefinition();
            var openParameters = definition.GetParameters();
            if (openParameters.Length == 0)
                return null;

            var count = Math.Min(arguments.Count, openParameters.Length);

            // Skip when no argument carries names (unshaped arguments only matter to erase names below).
            var anyShape = false;
            for (var i = 0; i < count; i++)
                if (getShape(arguments[i]) != null)
                {
                    anyShape = true;
                    break;
                }

            if (!anyShape)
                return null;

            var bindings = new Dictionary<Type, ValueTupleShape?>();
            var bound = new HashSet<Type>();
            for (var i = 0; i < count; i++)
                BindShape(openParameters[i].ParameterType, getShape(arguments[i]), bindings, bound, !typeof(Delegate).IsAssignableFrom(arguments[i].Type), nameComparison, 0);

            return BuildShape(definition.ReturnType, bindings, 0);
        }

        // Instance member on a generic type (e.g. List<(...)> indexer): bind the declaring type's parameters from the instance shape, then substitute into the open member's return type.
        if (instance != null && getShape(instance) is { Args: { } instanceArgs } && method.DeclaringType is { IsGenericType: true } closedDeclaringType)
        {
            var openDeclaringType = closedDeclaringType.GetGenericTypeDefinition();
            var openMember = FindOpenMethod(openDeclaringType, method);
            if (openMember == null)
                return null;

            var openArgs = openDeclaringType.GetGenericArguments();
            Dictionary<Type, ValueTupleShape?>? bindings = null;

            var count = Math.Min(openArgs.Length, instanceArgs.Length);
            for (var i = 0; i < count; i++)
                if (openArgs[i].IsGenericParameter && instanceArgs[i] != null)
                {
                    bindings ??= new Dictionary<Type, ValueTupleShape?>();
                    bindings[openArgs[i]] = instanceArgs[i];
                }

            return bindings == null ? null : BuildShape(openMember.ReturnType, bindings, 0);
        }

        return null;

        // Open member (with T, not a concrete type) matched by metadata token; GetGenericMethodDefinition only
        // fits methods with their own type parameters.
        static MethodInfo? FindOpenMethod(Type openDeclaringType, MethodInfo closedMethod)
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var candidate in openDeclaringType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                if (candidate.MetadataToken == closedMethod.MetadataToken)
                    return candidate;

            return null;
        }
    }

    /// <summary>
    /// Binds an argument shape to the generic parameters in <paramref name="openType"/>.
    /// </summary>
    public static void BindShape(Type openType, ValueTupleShape? shape, Dictionary<Type, ValueTupleShape?> bindings, HashSet<Type> bound, bool erasing, StringComparison nameComparison, int depth)
    {
        if (depth > 16)
            return;

        if (openType.IsGenericParameter)
        {
            if (erasing)
            {
                // Value argument: first binding wins; later ones merge, so a name survives only where all
                // agree (a null/unnamed contribution erases it), like C# tuple-name inference.
                if (bound.Add(openType))
                    bindings[openType] = shape;
                else
                    bindings[openType] = MergeShapes(bindings.TryGetValue(openType, out var existing) ? existing : null, shape, nameComparison);
            }
            else if (shape != null && bound.Add(openType))
            {
                // Delegate argument (e.g. a lambda's input): adds a shape but never erases value-argument names.
                bindings[openType] = shape;
            }
            return;
        }

        var children = GetStructuralChildren(openType);
        if (children == null)
            return;

        // Walk even for a null shape, so an unnamed argument can erase another argument's names.
        for (var i = 0; i < children.Length; i++)
        {
            var childShape = shape?.Args != null && i < shape.Args.Length ? shape.Args[i] : null;
            BindShape(children[i], childShape, bindings, bound, erasing, nameComparison, depth + 1);
        }
    }

    /// <summary>Builds a shape for <paramref name="openType"/> by substituting the bound generic parameters.</summary>
    private static ValueTupleShape? BuildShape(Type openType, Dictionary<Type, ValueTupleShape?> bindings, int depth)
    {
        if (depth > 16)
            return null;

        if (openType.IsGenericParameter)
            return bindings.TryGetValue(openType, out var shape) ? shape : null;

        var children = GetStructuralChildren(openType);
        if (children == null)
            return null;

        ValueTupleShape?[]? argShapes = null;

        for (var i = 0; i < children.Length; i++)
        {
            var childShape = BuildShape(children[i], bindings, depth + 1);

            if (childShape != null)
            {
                argShapes ??= new ValueTupleShape?[children.Length];
                argShapes[i] = childShape;
            }
        }

        return argShapes != null
            ? new ValueTupleShape { Args = argShapes }
            : null;
    }

    /// <summary>
    /// Structural children of a type: array element, or generic type arguments (e.g. IEnumerable&lt;T&gt; -> [T]).
    /// </summary>
    private static Type[]? GetStructuralChildren(Type type)
    {
        if (type.IsArray)
        {
            var element = type.GetElementType();
            return element != null ? [element] : null;
        }

        return type.IsGenericType ? type.GetGenericArguments() : null;
    }
}
