using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TagBites.Expressions.Extensions;
using TagBites.Utils;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    public override Expression? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var instance = Visit(node.Expression);
        if (instance == null)
        {
            // The prefix is not a value, try to interpret as a namespace-qualified type
            if (TryResolveNamespaceQualifiedType(node) is { } qualifiedType)
            {
                FirstError = null;
                return Expression.Constant(qualifiedType);
            }

            return null;
        }

        var name = node.Name.Identifier.Text;
        return ResolveCustomMember(instance, name)
               ?? ResolveMember(node, instance, name);
    }
    public override Expression? VisitMemberBindingExpression(MemberBindingExpressionSyntax node)
    {
        var instance = Pop(node);
        if (instance == null)
            return null;

        var name = node.Name.Identifier.Text;
        return ResolveCustomMember(instance, name)
               ?? ResolveMember(node, instance, name);
    }
    public override Expression? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
    {
        var instance = Visit(node.Expression);
        if (instance == null)
            return null;

        // Resolve Member
        var valueInstance = instance;

        if (instance.Type.IsValueType && Nullable.GetUnderlyingType(instance.Type) != null)
            valueInstance = Expression.MakeMemberAccess(instance, instance.Type.GetProperty("Value")!);

        Push(valueInstance);
        var whenNotNull = Visit(node.WhenNotNull);
        if (whenNotNull == null)
            return null;

        return new ConditionalAccessExpression(instance, whenNotNull).Reduce();
    }

    public override Expression? VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        var arguments = node.ArgumentList.Arguments;

        // Index-from-end (x[^n]), uses collection length to avoid relying on System.Index.
        if (arguments.Count == 1
            && arguments[0].Expression is PrefixUnaryExpressionSyntax { OperatorToken.RawKind: (int)SyntaxKind.CaretToken } fromEnd)
        {
            if (Visit(node.Expression) is not { } instance)
                return null;

            if (Visit(fromEnd.Operand) is not { } offset)
                return null;

            if (offset.Type != typeof(int))
            {
                if (TryConvertExpression(offset, typeof(int)) is not { } converted)
                    return ToError(fromEnd.Operand, "Index must be convertible to int.");

                offset = converted;
            }

            // Capture the receiver so it is evaluated once (both the length and the access use it).
            var receiver = Expression.Variable(instance.Type, "receiver");

            // Length
            Expression length;
            if (instance.Type.IsArray)
            {
                if (instance.Type.GetArrayRank() != 1)
                    return ToError(node, "Index from end is not supported for multidimensional arrays.");

                length = Expression.ArrayLength(receiver);
            }
            else
            {
                var property = GetCountProperty(instance.Type, "Length")
                               ?? GetCountProperty(instance.Type, "Count");
                if (property == null)
                    return ToError(node, $"Index from end requires a 'Length' or 'Count' property on type '{instance.Type.GetFriendlyTypeName()}'.");

                length = Expression.MakeMemberAccess(receiver, property);
            }

            // Access
            var access = ResolveItemCall(node, receiver, [Expression.Subtract(length, offset)]);
            if (access == null)
                return null;

            return Expression.Block([receiver], Expression.Assign(receiver, instance), access);
        }

        // Parameters
        var parameters = ResolveParameters(node.ArgumentList.Arguments);
        if (parameters == null)
            return null;

        // Find instance, type, metod name
        var instanceExpression = Visit(node.Expression);
        if (instanceExpression == null)
            return null;

        return ResolveItemCall(node, instanceExpression, parameters, GetArgumentNames(node.ArgumentList.Arguments));
    }
    public override Expression? VisitElementBindingExpression(ElementBindingExpressionSyntax node)
    {
        // Parameters
        var parameters = ResolveParameters(node.ArgumentList.Arguments);
        if (parameters == null)
            return null;

        // Find instance, type, metod name
        var instanceExpression = Pop(node);
        if (instanceExpression == null)
            return null;

        return ResolveItemCall(node, instanceExpression, parameters, GetArgumentNames(node.ArgumentList.Arguments));
    }

    public override Expression? VisitIdentifierName(IdentifierNameSyntax node)
    {
        var name = node.Identifier.Text;

        // Variables
        if (_variables != null)
        {
            var (varType, _, varIndex) = _variables.FirstOrDefault(x => string.Equals(x.Name, name, _nameComparison));
            if (varType != null)
                return Expression.Call(_variableContextParameter!, s_lvcGetValue.MakeGenericMethod(varType), Expression.Constant(varIndex));
        }

        // Local parameter
        if (_nestedParameters != null)
            for (var i = _nestedParameters.Count - 1; i >= 0; i--)
            {
                var nested = _nestedParameters[i];
                if (string.Equals(nested.Name, name, _nameComparison) && nested != _context.ThisParameter)
                    return nested;
            }

        // Parameter
        var parameter = FindParameter(name);

        if (parameter != null)
            return parameter;

        // This
        if (_context.ThisParameter != null)
        {
            var expression = ResolveCustomMember(_context.ThisParameter, name)
                             ?? ResolveMember(node, _context.ThisParameter, name, false);
            if (expression != null)
                return expression;
        }

        // Members
        if (_context.GlobalMembers?.TryGetValue(name, out var item) == true)
            return Expression.Constant(item.Value, GetGlobalMemberType(name, item));

        // Static type
        var type = ResolveType(node, name);
        if (type != null)
            return Expression.Constant(type);

        // Static import
        if (_context.StaticImports?.Count > 0)
            foreach (var import in _context.StaticImports.Values)
                if (ResolveMember(node, Expression.Constant(import), name, setErrorWhenNotFound: false) is { } importedMember)
                    return importedMember;

        // Unknown
        return string.IsNullOrEmpty(name)
            ? ToError(node, "Missing identifier.")
            : ToError(node, $"Unknown identifier '{name}'.");
    }
    public override Expression? VisitThisExpression(ThisExpressionSyntax node)
    {
        return _context.ThisParameter ?? ToError(node, "Keyword 'this' is not valid in a static property or method.");
    }

    private Expression? ResolveMember(SyntaxNode node, Expression expression, string name, bool setErrorWhenNotFound = true)
    {
        var staticType = expression.Type != typeof(Type)
            ? (expression as ConstantExpression)?.Value as Type
            : null;
        var expressionType = staticType ?? expression.Type;

        // ValueTuple
        if (staticType == null && IsValueTupleType(expressionType))
        {
            var tupleShape = GetTupleShape(expression);
            var index = tupleShape?.GetRealField(name, _nameComparison) is ({ } _, var aliasIndex)
                ? aliasIndex
                : TryGetTupleItemIndex(name) ?? -1;

            if (index >= 0 && BuildTupleElementAccess(expression, index) is { } access)
            {
                SetTupleShape(access, tupleShape?.Args?.Length > index ? tupleShape.Args[index] : null);
                return access;
            }
        }

        // From instance or interface
        {
            var members = GetTypeMembers(expressionType, name, includeInterfaces: staticType == null);
            switch (members.Length)
            {
                case 1:
                    return Expression.MakeMemberAccess(staticType != null ? null : expression, members[0]);
                case > 1:
                    return setErrorWhenNotFound ? ToError(node, $"More then one member with name {name}.") : null;
            }
        }

        // Anonymous object
        if (staticType == null && _anonymousObjects != null && typeof(AnonymousObject).IsAssignableFrom(expressionType))
        {
            var (_, shape) = _anonymousObjects.FirstOrDefault(x => x.SlotType == expressionType);
            if (shape != null)
            {
                var (memberName, memberType, memberShape) = shape.FirstOrDefault(x => string.Equals(x.Name, name, _nameComparison));
                if (memberType == null)
                    return setErrorWhenNotFound ? ToError(node, $"'{name}' is not a member of this anonymous object.") : null;

                var asSlotDictionary = Expression.Convert(expression, typeof(IDictionary<string, object>));
                var slotValue = Expression.MakeIndex(asSlotDictionary, s_anonymousObjectIndexer, [Expression.Constant(memberName)]);
                var result = Expression.Convert(slotValue, memberType);

                // Save member value's tuple shape stored in an anonymous member
                SetTupleShape(result, memberShape);

                return result;
            }
        }

        return setErrorWhenNotFound ? ToError(node, $"Unknown member {name}.") : null;
    }
    private Expression? ResolveCustomMember(Expression expression, string name)
    {
        if (_options.CustomPropertyResolver is not { } resolver)
            return null;

        _resolverContext ??= new MemberResolverContext(this);
        _resolverContext.Switch(_extensionInstance, expression, name);

        if (_fullMemberPath?.TryGetValue(expression, out var fullMemberPath) == true)
            _resolverContext.MemberFullPath = fullMemberPath + "." + name;

        var next = resolver(_resolverContext);
        if (next != null)
        {
            _fullMemberPath ??= new Dictionary<Expression, string>();
            _fullMemberPath[next] = _resolverContext.MemberFullPath?.Length > 0 ? _resolverContext.MemberFullPath : name;
        }

        return next;
    }
    private Expression? ResolveItemCall(SyntaxNode relatedNode, Expression instanceExpression, IList<Expression> parameters, IReadOnlyList<string?>? argumentNames = null)
    {
        var instanceType = instanceExpression.Type;
        if (instanceType.IsArray)
            return PropagateElementTypeInfo(instanceExpression, Expression.ArrayAccess(instanceExpression, parameters));

        // Select method override
        var methods = GetIndexers(instanceType);

        if (methods.Count > 0 && TryResolveMethodCall(relatedNode, instanceExpression, parameters, methods, out var expression, argumentNames))
            return expression != null ? PropagateElementTypeInfo(instanceExpression, expression) : null;

        return ToError(relatedNode, "Indexer not found for this arguments.");
    }

    private static PropertyInfo? GetCountProperty(Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property == null && type.IsInterface)
            foreach (var i in type.GetInterfaces())
                if ((property = i.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)) != null)
                    break;

        return property?.PropertyType == typeof(int) && property.GetMethod is { IsPublic: true } ? property : null;
    }

    private ParameterExpression? FindParameter(string name)
    {
        var parameters = _context.Parameters;

        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter != _context.ThisParameter && string.Equals(parameter.Name, name, _nameComparison))
                return parameter;
        }

        return null;
    }
    private bool HasParameter(string name)
    {
        var parameters = _context.Parameters;

        for (var i = parameters.Length - 1; i >= 0; i--)
            if (string.Equals(parameters[i].Name, name, _nameComparison))
                return true;

        return false;
    }
    private bool IsKnownIdentifier(string name)
    {
        return _variables?.Any(x => string.Equals(x.Name, name, _nameComparison)) == true
               || _nestedParameters?.Any(x => string.Equals(x.Name, name, _nameComparison)) == true
               || HasParameter(name)
               || _context.GlobalMembers?.TryGetValue(name, out _) == true;
    }
    internal static Type GetGlobalMemberType(string name, (Type? Type, object? Value) member)
    {
        if (member is { Type: not null, Value: not null } && !member.Type.IsAssignableFrom(member.Value.GetType()))
            throw new ArgumentException($"Member value is not type of member type. Member '{name}'.");

        return member.Type ?? member.Value?.GetType() ?? typeof(object);
    }

    private class MemberResolverContext(ExpressionBuilder visitor) : IExpressionMemberResolverContext
    {
        private string? _memberFullPath;

        public Expression Instance { get; private set; } = null!;
        public object? InstanceTypeInfo { get; private set; }

        public string MemberName { get; private set; } = null!;
        public string? MemberFullPath
        {
            get
            {
                if (_memberFullPath == null)
                {
                    var names = new List<string>(2) { MemberName };
                    var isOk = true;
                    var i = Instance;

                    while (isOk)
                    {
                        if (i is ParameterExpression p)
                        {
                            names.Add(p.Name);
                            break;
                        }

                        if (i is MemberExpression ma)
                        {
                            names.Add(ma.Member.Name);
                            i = ma.Expression;
                            continue;
                        }

                        isOk = false;
                    }

                    if (isOk)
                        names.Reverse();

                    _memberFullPath = isOk
                        ? string.Join(".", names)
                        : string.Empty;
                }

                return _memberFullPath != string.Empty
                    ? _memberFullPath
                    : null;
            }
            set => _memberFullPath = value;
        }

        public void Switch(Expression? expressionInstance, Expression instance, string memberName)
        {
            Instance = instance;
            InstanceTypeInfo = ExtractTypeInfo(instance) ?? (instance is ParameterExpression ? ExtractTypeInfo(expressionInstance) : null);

            MemberName = memberName;
            _memberFullPath = null;
        }

        public ParameterExpression GetParameter(string name) => visitor._context.Parameters.First(x => x.Name == name);
        public Expression IncludeTypeInfo(Expression expression, object typeInfo) => WrapWithTypeInfo(expression, typeInfo);
    }
}
