using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TagBites.Utils;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    public override Expression? VisitTypeOfExpression(TypeOfExpressionSyntax node)
    {
        var type = ResolveType(node.Type);
        if (type == null)
            return null;

        return Expression.Constant(type, typeof(Type));
    }
    public override Expression? VisitNullableType(NullableTypeSyntax node)
    {
        var type = ResolveType(node.ElementType);
        if (type == null)
            return null;

        if (!type.IsValueType || IsNullableType(type))
            return ToError(node, "Invalid nullable type.");

        return Expression.Constant(typeof(Nullable<>).MakeGenericType(type));
    }
    public override Expression? VisitPredefinedType(PredefinedTypeSyntax node)
    {
        var type = ResolveType(node);
        return type != null
            ? Expression.Constant(type)
            : null;
    }

    public override Expression? VisitGenericName(GenericNameSyntax node)
    {
        var type = ResolveType(node);
        return type != null
            ? Expression.Constant(type)
            : null;
    }

    private Type? ResolveType(TypeSyntax type)
    {
        switch (type)
        {
            case NullableTypeSyntax nts:
                {
                    var typeArgument = ResolveType(nts.ElementType);
                    return typeArgument != null
                        ? typeof(Nullable<>).MakeGenericType(typeArgument)
                        : null;
                }

            case PredefinedTypeSyntax pts:
                return (SyntaxKind)pts.Keyword.RawKind switch
                {
                    SyntaxKind.BoolKeyword => typeof(bool),
                    SyntaxKind.ByteKeyword => typeof(byte),
                    SyntaxKind.SByteKeyword => typeof(sbyte),
                    SyntaxKind.ShortKeyword => typeof(short),
                    SyntaxKind.UShortKeyword => typeof(ushort),
                    SyntaxKind.IntKeyword => typeof(int),
                    SyntaxKind.UIntKeyword => typeof(uint),
                    SyntaxKind.LongKeyword => typeof(long),
                    SyntaxKind.ULongKeyword => typeof(ulong),
                    SyntaxKind.DoubleKeyword => typeof(double),
                    SyntaxKind.FloatKeyword => typeof(float),
                    SyntaxKind.DecimalKeyword => typeof(decimal),
                    SyntaxKind.StringKeyword => typeof(string),
                    SyntaxKind.CharKeyword => typeof(char),
                    SyntaxKind.VoidKeyword => typeof(void),
                    SyntaxKind.ObjectKeyword => typeof(object),
                    _ => ToTypeError(type, null)
                };

            case ArrayTypeSyntax arrayType:
                {
                    var elementType = ResolveType(arrayType.ElementType);
                    if (elementType == null)
                        return null;

                    for (var i = arrayType.RankSpecifiers.Count - 1; i >= 0; i--)
                    {
                        var rank = arrayType.RankSpecifiers[i].Rank;
                        elementType = rank == 1 ? elementType.MakeArrayType() : elementType.MakeArrayType(rank);
                    }

                    return elementType;
                }

            case GenericNameSyntax genericName:
                {
                    var arguments = genericName.TypeArgumentList.Arguments;

                    // Unbound generic (typeof(List<>), typeof(Dictionary<,>)): return the open definition.
                    if (arguments[0] is OmittedTypeArgumentSyntax)
                    {
                        var definition = ResolveType(type, genericName.Identifier.Text, arguments.Count);
                        return definition == null || definition.IsGenericTypeDefinition
                            ? definition
                            : ToTypeError(type, null);
                    }

                    var elements = new Type[arguments.Count];
                    for (var i = 0; i < arguments.Count; i++)
                    {
                        var elementType = ResolveType(arguments[i]);
                        if (elementType == null)
                            return null;

                        elements[i] = elementType;
                    }

                    var genericType = ResolveType(type, genericName.Identifier.Text, elements.Length, elements);
                    return genericType != null
                        ? TryCloseGenericType(genericType, elements) ?? ToTypeError(type, null)
                        : null;
                }

            case QualifiedNameSyntax { Right: GenericNameSyntax gen } name:
                {
                    var arguments = gen.TypeArgumentList.Arguments;

                    if (arguments[0] is OmittedTypeArgumentSyntax)
                    {
                        return TryResolveTypeByName(gen.Identifier.Text, arguments.Count) is { IsGenericTypeDefinition: true } open && open.Namespace == name.Left.ToString()
                            ? open
                            : ToTypeError(type, null);
                    }

                    var elements = new Type[arguments.Count];
                    for (var i = 0; i < arguments.Count; i++)
                    {
                        var elementType = ResolveType(arguments[i]);
                        if (elementType == null)
                            return null;

                        elements[i] = elementType;
                    }

                    if (TryResolveTypeByName(gen.Identifier.Text, arguments.Count, elements) is not { } resolved || resolved.Namespace != name.Left.ToString())
                        return ToTypeError(type, null);

                    return TryCloseGenericType(resolved, elements) ?? ToTypeError(type, null);
                }

            case QualifiedNameSyntax { Right: IdentifierNameSyntax id } name:
                {
                    Type? ret;
                    if (_options.TypeResolver?.Invoke(name.ToString()) is { } resolved)
                        ret = resolved;
                    else
                    {
                        if (TryResolveTypeByName(id.Identifier.Text) is { } type1 && type1.Namespace == name.Left.ToString())
                            ret = type1;
                        else
                        {
                            ret = null;
                        }
                    }

                    // Nested type, e.g. List<int>.Enumerator
                    if (ret == null)
                    {
                        var outerType = name.Left switch
                        {
                            GenericNameSyntax => ResolveType(name.Left),
                            IdentifierNameSyntax leftId => TryResolveTypeByName(leftId.Identifier.Text),
                            _ => null
                        };

                        ret = outerType?.GetNestedType(id.Identifier.Text, BindingFlags.Public);

                        if (ret is { IsGenericTypeDefinition: true } && outerType is { IsGenericType: true, IsGenericTypeDefinition: false })
                            ret = ret.MakeGenericType(outerType.GetGenericArguments());
                    }

                    return ret
                           ?? ToTypeError(type, null);
                }

            case IdentifierNameSyntax name:
                return ResolveType(name, name.Identifier.Text);

            default:
                return ToTypeError(type, null);
        }
    }
    private Type? ResolveType(SyntaxNode relatedNode, string typeName, int genericArguments = 0, Type[]? genericArgumentTypes = null)
    {
        return TryResolveTypeByName(typeName, genericArguments, genericArgumentTypes)
               ?? ToTypeError(relatedNode, typeName);
    }

    private Type? TryResolveTypeByName(string typeName, int genericArguments = 0, Type[]? genericArgumentTypes = null)
    {
        if (genericArguments > 0)
            typeName += "'" + genericArguments;

        if (_resultType is { } resultType && resultType != typeof(object))
        {
            resultType = Nullable.GetUnderlyingType(resultType) ?? resultType;
            if (string.Equals(resultType.Name, typeName, _nameComparison))
                return resultType;
        }

        if (_context.IncludedTypes is { Count: > 0 } includedTypes)
        {
            // A closed generic is stored under a key with its exact arguments, an open definition under the arity key
            if (genericArgumentTypes != null
                && includedTypes.TryGetValue(TypeCollection.GetKey(typeName, genericArgumentTypes), out var closedType)
                && closedType != null)
            {
                return closedType;
            }

            if (includedTypes.TryGetValue(typeName, out var type) && type != null)
                return type;
        }

        foreach (var parameter in _context.Parameters)
            if (string.Equals(parameter.Type.Name, typeName, _nameComparison))
                return parameter.Type;

        if (!_options.IgnoreBuiltInTypes && TryResolveBuiltInType(typeName) is { } builtInType)
            return builtInType;

        if (_options.TypeResolver?.Invoke(typeName) is { } resolvedType)
            return resolvedType;

        return null;
    }
    protected Type? TryResolveNamespaceQualifiedType(ExpressionSyntax node)
    {
        if (TryGetDottedName(node, out var fullName, out var simpleName))
        {
            var namespacePrefix = fullName.Substring(0, fullName.Length - simpleName.Length - 1);
            if (_options.TypeResolver?.Invoke(fullName) is { } resolved)
                return resolved;

            if (TryResolveTypeByName(simpleName) is { } type && type.Namespace == namespacePrefix)
                return type;
        }

        return null;

        static bool TryGetDottedName(ExpressionSyntax node, out string fullName, out string simpleName)
        {
            switch (node)
            {
                case IdentifierNameSyntax id:
                    fullName = simpleName = id.Identifier.Text;
                    return true;

                case MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression, Name: IdentifierNameSyntax name } ma
                    when TryGetDottedName(ma.Expression, out var prefix, out _):
                    simpleName = name.Identifier.Text;
                    fullName = prefix + "." + simpleName;
                    return true;

                default:
                    fullName = simpleName = string.Empty;
                    return false;
            }
        }
    }
    private static Type? TryResolveBuiltInType(string typeName)
    {
        return typeName switch
        {
            // CLR names of the built-in C# types
            "Boolean" => typeof(bool),
            "Byte" => typeof(byte),
            "SByte" => typeof(sbyte),
            "Int16" => typeof(short),
            "UInt16" => typeof(ushort),
            "Int32" => typeof(int),
            "UInt32" => typeof(uint),
            "Int64" => typeof(long),
            "UInt64" => typeof(ulong),
            "Single" => typeof(float),
            "Double" => typeof(double),
            "Decimal" => typeof(decimal),
            "Char" => typeof(char),
            "String" => typeof(string),
            "Object" => typeof(object),

            // Time
            "TimeSpan" => typeof(TimeSpan),
            "DateTime" => typeof(DateTime),
            "DateTimeOffset" => typeof(DateTimeOffset),
            "DateTimeKind" => typeof(DateTimeKind),
            "DayOfWeek" => typeof(DayOfWeek),

            // Text
            "StringComparison" => typeof(StringComparison),
            "StringSplitOptions" => typeof(StringSplitOptions),
            "CultureInfo" => typeof(CultureInfo), // Used for string formating

            // Math
            "MidpointRounding" => typeof(MidpointRounding),
            "Math" => typeof(Math),

            // Common types
            "Guid" => typeof(Guid),
            "KeyValuePair'2" => typeof(KeyValuePair<,>),

            // Collections
            "Enumerable" => typeof(Enumerable),

            "List'1" => typeof(List<>),
            "Dictionary'2" => typeof(Dictionary<,>),
            "HashSet'1" => typeof(HashSet<>),

            "IList'1" => typeof(IList<>),
            "IEnumerable'1" => typeof(IEnumerable<>),
            "ICollection'1" => typeof(ICollection<>),
            "IReadOnlyList'1" => typeof(IReadOnlyList<>),
            "IReadOnlyCollection'1" => typeof(IReadOnlyCollection<>),
            "IDictionary'2" => typeof(IDictionary<,>),
            "IReadOnlyDictionary'2" => typeof(IReadOnlyDictionary<,>),
            "ISet'1" => typeof(ISet<>),

            // Other
            "Convert" => typeof(Convert),

            _ => null
        };
    }

    private static Type? TryCloseGenericType(Type type, Type[] arguments)
    {
        if (type.IsGenericTypeDefinition)
            return type.MakeGenericType(arguments);

        // A closed generic included as typeof(SortedSet<int>) is available only with those exact arguments
        return AreTypesEqual(type.GetGenericArguments(), arguments) ? type : null;
    }

    private static Expression WrapWithTypeInfo(Expression expression, object typeInfo)
    {
        var method = s_typeInfoWrapper.MakeGenericMethod(expression.Type);
        return Expression.Call(method, expression, Expression.Constant(typeInfo));
    }
    private Expression PropagateElementTypeInfo(Expression receiver, Expression result)
    {
        // Element type info is only attached by CustomPropertyResolver
        if (_options.CustomPropertyResolver == null)
            return result;

        var typeInfo = ExtractTypeInfo(receiver);
        if (typeInfo == null)
            return result;

        var receiverElementType = GetEnumerableElementType(receiver.Type);
        if (receiverElementType == null)
            return result;

        var isSameShape = GetEnumerableElementType(result.Type) == receiverElementType // sequence -> sequence, e.g. Where/OrderBy/SelectMany
                          || result.Type == receiverElementType;                                     // sequence -> single element, e.g. FirstOrDefault/ElementAt/indexer

        return isSameShape ? WrapWithTypeInfo(result, typeInfo) : result;

        static Type? GetEnumerableElementType(Type type)
        {
            var elementTypes = TypeUtils.GetGenericArguments(type, typeof(IEnumerable<>));
            return elementTypes.Length > 0 ? elementTypes[0] : null;
        }
    }
    private static object? ExtractTypeInfo(Expression? expression)
    {
        if (expression is MethodCallExpression { Method.IsGenericMethod: true } mc && mc.Method.GetGenericMethodDefinition() == s_typeInfoWrapper)
            return ((ConstantExpression)mc.Arguments[1]).Value;

        return null;
    }
    // ReSharper disable once UnusedParameter.Local
    private static T TypeInfoWrapper<T>(T value, object typeInfo) => value;

    private static bool IsNullableType(Type type) => Nullable.GetUnderlyingType(type) != null;
    private static bool IsValueTupleType(Type type)
    {
        return type is { IsGenericType: true, Namespace: "System" }
               && type.Name.StartsWith("ValueTuple`", StringComparison.Ordinal);
    }
    private static bool AreTypesEqual(Type[] a, Type[] b)
    {
        if (a.Length != b.Length)
            return false;

        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                return false;

        return true;
    }
}
