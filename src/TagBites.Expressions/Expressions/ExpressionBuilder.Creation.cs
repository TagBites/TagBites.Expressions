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
    public override Expression? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var type = ResolveType(node.Type);
        if (type == null)
            return null;

        return CreateObject(node, type, node.ArgumentList, node.Initializer);
    }
    public override Expression? VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        return _targetType != null
            ? CreateObject(node, _targetType, node.ArgumentList, node.Initializer)
            : new DelayNewExpression(node);
    }

    public override Expression? VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
    {
        var elementType = ResolveType(node.Type.ElementType);
        if (elementType == null)
            return null;

        // Jagged array (int[][], int[2][], ...)
        for (var i = node.Type.RankSpecifiers.Count - 1; i >= 1; i--)
        {
            var innerSpecifier = node.Type.RankSpecifiers[i];

            // ReSharper disable once LoopCanBeConvertedToQuery
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var j = 0; j < innerSpecifier.Sizes.Count; j++)
                if (innerSpecifier.Sizes[j] is not OmittedArraySizeExpressionSyntax)
                    return ToError(innerSpecifier, "Only the outermost dimension of a jagged array can have a size.");

            var innerRank = innerSpecifier.Rank;
            elementType = innerRank == 1 ? elementType.MakeArrayType() : elementType.MakeArrayType(innerRank);
        }

        var rankSpecifier = node.Type.RankSpecifiers[0];
        var rank = rankSpecifier.Rank;

        // With initializer, optional sizes inferred from the initializer
        if (node.Initializer != null)
        {
            // Simple array
            if (rank == 1)
            {
                var initializerExpressions = node.Initializer.Expressions;
                var expressions = new Expression[initializerExpressions.Count];

                for (var i = 0; i < expressions.Length; i++)
                {
                    var expression = initializerExpressions[i];
                    var exp = Visit(expression);
                    if (exp == null)
                        return null;

                    if (!EnsureArgumentType(elementType, ref exp))
                        return ToError(expression, $"Cannot convert array element to '{elementType.GetFriendlyTypeName()}'.");

                    expressions[i] = exp;
                }

                var array = Expression.NewArrayInit(elementType, expressions);
                if (expressions.Length > 0)
                    SetSequenceElementShape(array, GetTupleShape(expressions[0]));

                return array;
            }

            // Multidimensional array (e.g. new int[,] { { 1, 2 }, { 3, 4 } }) 
            var dimensions = new List<int>();
            var elements = new List<Expression>();

            if (!CollectMultiDimElements(node.Initializer, 0, dimensions, elements))
                return null;

            return CreateMultiDimArray(elementType, rank, dimensions, elements);
        }

        // Without initializer: every dimension needs an explicit size
        if (rankSpecifier.Sizes[0] is OmittedArraySizeExpressionSyntax)
            return ToError(node.Type, "Array creation requires either explicit sizes or an initializer.");

        var bounds = new Expression[rank];
        for (var i = 0; i < rank; i++)
        {
            var sizeExpression = Visit(rankSpecifier.Sizes[i]);
            if (sizeExpression == null)
                return null;

            if (TryConvertExpression(sizeExpression, typeof(int)) is not { } bound)
                return ToError(rankSpecifier.Sizes[i], "Array size must be convertible to int.");

            bounds[i] = bound;
        }

        return Expression.NewArrayBounds(elementType, bounds);

        bool CollectMultiDimElements(InitializerExpressionSyntax initializer, int depth, List<int> dimensions, List<Expression> elements)
        {
            var count = initializer.Expressions.Count;

            // All sub-initializers at the same depth must have the same length (rectangular).
            if (dimensions.Count <= depth)
                dimensions.Add(count);
            else if (dimensions[depth] != count)
            {
                ToError(initializer, "Array initializer has inconsistent dimensions.");
                return false;
            }

            if (depth == rank - 1)
            {
                foreach (var expression in initializer.Expressions)
                {
                    var exp = Visit(expression);
                    if (exp == null)
                        return false;

                    if (!EnsureArgumentType(elementType, ref exp))
                    {
                        ToError(expression, $"Cannot convert array element to '{elementType.GetFriendlyTypeName()}'.");
                        return false;
                    }

                    elements.Add(exp);
                }
            }
            else
            {
                foreach (var expression in initializer.Expressions)
                {
                    if (expression is not InitializerExpressionSyntax nested)
                    {
                        ToError(expression, "Array initializer has inconsistent dimensions.");
                        return false;
                    }

                    if (!CollectMultiDimElements(nested, depth + 1, dimensions, elements))
                        return false;
                }
            }

            return true;
        }
    }
    public override Expression? VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
    {
        // new[] { ... } is rank 1, new[,] { ... } rank 2, etc.
        var rank = node.Commas.Count + 1;
        var elements = new List<Expression>();
        var dimensions = new List<int>();
        var candidateTypes = new List<Type>();

        if (!CollectImplicitElements(node.Initializer, 0))
            return null;

        var elementType = candidateTypes.Count switch
        {
            0 => null,
            1 => candidateTypes[0],
            _ => FindBestCommonType(candidateTypes)
        };
        if (elementType == null)
            return ToError(node, "Type not found for implicit array creation.");

        // Null literals and narrower elements take the inferred element type
        for (var i = 0; i < elements.Count; i++)
            if (elements[i].Type != elementType || IsNullLiteral(elements[i]))
            {
                var element = elements[i];
                if (!EnsureArgumentType(elementType, ref element))
                    return ToError(node, "Type not found for implicit array creation.");

                elements[i] = element;
            }

        if (rank != 1)
            return CreateMultiDimArray(elementType, rank, dimensions, elements);

        var array = Expression.NewArrayInit(elementType, elements);
        if (elements.Count > 0)
            SetSequenceElementShape(array, GetTupleShape(elements[0]));

        return array;

        bool CollectImplicitElements(InitializerExpressionSyntax initializer, int depth)
        {
            var count = initializer.Expressions.Count;

            // All sub-initializers at the same depth must have the same length (rectangular).
            if (dimensions.Count <= depth)
                dimensions.Add(count);
            else if (dimensions[depth] != count)
            {
                ToError(initializer, "Array initializer has inconsistent dimensions.");
                return false;
            }

            if (depth == rank - 1)
            {
                foreach (var expression in initializer.Expressions)
                {
                    var exp = Visit(expression);
                    if (exp == null)
                        return false;

                    if (!IsNullLiteral(exp) && !candidateTypes.Contains(exp.Type))
                        candidateTypes.Add(exp.Type);

                    elements.Add(exp);
                }
            }
            else
            {
                foreach (var expression in initializer.Expressions)
                {
                    if (expression is not InitializerExpressionSyntax nested)
                    {
                        ToError(expression, "Array initializer has inconsistent dimensions.");
                        return false;
                    }

                    if (!CollectImplicitElements(nested, depth + 1))
                        return false;
                }
            }

            return true;
        }
    }

    public override Expression? VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpressionSyntax node)
    {
        var members = new List<(string Name, Expression Value)>();
        var shape = new List<(string Name, Type Type, ValueTupleShape? TupleShape)>();

        foreach (var member in node.Initializers)
        {
            // Name
            string? name;
            if (member.NameEquals != null)
                name = member.NameEquals.Name.Identifier.Text;
            else
            {
                name = member.Expression switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                    _ => null
                };
            }

            if (name == null)
                return ToError(member, "Anonymous object member requires an explicit name (e.g. `Name = value`).");

            if (members.Any(x => x.Name == name))
                return ToError(member, $"Member '{name}' is already declared.");

            // Value
            var valueExpression = Visit(member.Expression);
            if (valueExpression == null)
                return null;

            members.Add((name, valueExpression));
            shape.Add((name, valueExpression.Type, GetTupleShape(valueExpression)));
        }

        var (backingType, canonicalShape) = GetOrAssignAnonymousObjectSlot();

        var instanceVariable = Expression.Variable(backingType);
        var asDictionary = Expression.Convert(instanceVariable, typeof(IDictionary<string, object>));
        var constructor = backingType.GetConstructor(Type.EmptyTypes)!;

        // Store each member under its slot's canonical (first-registered) name, so all instances of a shared
        // slot use the same dictionary keys - required once IgnoreCase merges case-differing literals.
        var statements = new List<Expression> { Expression.Assign(instanceVariable, Expression.New(constructor)) };
        for (var i = 0; i < members.Count; i++)
            statements.Add(Expression.Call(asDictionary, s_anonymousObjectIndexer.SetMethod!, Expression.Constant(canonicalShape[i].Name), Expression.Convert(members[i].Value, typeof(object))));

        statements.Add(instanceVariable);

        return Expression.Block([instanceVariable], statements);

        (Type SlotType, List<(string Name, Type Type, ValueTupleShape? TupleShape)> Shape) GetOrAssignAnonymousObjectSlot()
        {
            if (_anonymousObjects != null)
                foreach (var existing in _anonymousObjects)
                    if (AnonymousObject.AnonymousShapesEqual(existing.Shape, shape, _nameComparison))
                        return existing;

            var newSlotType = AnonymousObject.GetAnonymousObjectType(_anonymousObjects?.Count ?? 0);
            _anonymousObjects ??= [];
            _anonymousObjects.Add((newSlotType, shape));

            return (newSlotType, shape);
        }
    }

    private Expression? CreateObject(SyntaxNode node, Type type, ArgumentListSyntax? argumentList, InitializerExpressionSyntax? initializer)
    {
        var previousTargetType = _targetType;
        _targetType = null;

        var args = argumentList != null ? ResolveParameters(argumentList.Arguments) : Array.Empty<Expression>();
        _targetType = previousTargetType;

        if (args == null)
            return null;

        var argumentNames = argumentList != null
            ? GetArgumentNames(argumentList.Arguments)
            : null;
        if (!TryResolveCall(node, null, args, type.GetConstructors(), out var created, argumentNames))
            return ToError(node, $"Constructor for '{type.GetFriendlyTypeName()}' not found.");

        if (created is not NewExpression instance)
        {
            // Ambiguity or another error already reported by the engine.
            return null;
        }

        // Initializer
        if (initializer?.Expressions.Count > 0)
        {
            if (initializer.IsKind(SyntaxKind.CollectionInitializerExpression))
                return CreateCollectionInitializer(instance, type, initializer);

            // An indexer element initializer (["k"] = v) cannot be expressed with MemberInit, so fall back to a block when any is present.
            // Plain member assignments keep the simpler MemberInit form.
            var hasIndexerInitializer = false;

            foreach (var item in initializer.Expressions)
                if (item is AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax })
                {
                    hasIndexerInitializer = true;
                    break;
                }

            if (hasIndexerInitializer)
                return CreateObjectInitializerBlock(instance, type, initializer, previousTargetType);

            var bindings = new List<MemberBinding>();

            foreach (var item in initializer.Expressions)
            {
                if (item is not AssignmentExpressionSyntax { Left: IdentifierNameSyntax identifier } ae)
                    return ToError(item);

                var memberName = identifier.Identifier.Text;
                var member = GetAssignMember(type, memberName);
                if (member == null)
                    return ToError(item, "Member not found.");

                _targetType = member is PropertyInfo pi ? pi.PropertyType : ((FieldInfo)member).FieldType;
                var expression = Visit(ae.Right);
                _targetType = previousTargetType;
                if (expression == null)
                    return null;

                bindings.Add(Expression.Bind(member, expression));
            }

            return Expression.MemberInit(instance, bindings);
        }

        return instance;
    }
    private Expression? CreateObjectInitializerBlock(NewExpression instance, Type type, InitializerExpressionSyntax initializer, Type? previousTargetType)
    {
        var instanceVariable = Expression.Variable(type, "obj");
        var statements = new List<Expression> { Expression.Assign(instanceVariable, instance) };

        foreach (var item in initializer.Expressions)
        {
            if (item is not AssignmentExpressionSyntax ae)
                return ToError(item);

            Expression target;
            Type memberType;

            switch (ae.Left)
            {
                case ImplicitElementAccessSyntax indexer:
                    {
                        _targetType = null;
                        var indexArguments = ResolveParameters(indexer.ArgumentList.Arguments);
                        _targetType = previousTargetType;

                        if (indexArguments == null)
                            return null;

                        var indexers = GetIndexers(type);
                        if (indexers.Count == 0
                            || !TryResolveMethodCall(indexer, instanceVariable, indexArguments, indexers, out var getCall, GetArgumentNames(indexer.ArgumentList.Arguments))
                            || getCall is not MethodCallExpression getterCall)
                        {
                            return ToError(indexer, "Indexer not found for this arguments.");
                        }

                        var indexerProperty = Array.Find(GetIndexerProperties(getterCall.Method.DeclaringType!), x => x.GetMethod == getterCall.Method);
                        if (indexerProperty is not { SetMethod.IsPublic: true })
                            return ToError(item, "Indexer has no accessible setter.");

                        target = Expression.Property(instanceVariable, indexerProperty, getterCall.Arguments.ToArray());
                        memberType = indexerProperty.PropertyType;
                        break;
                    }

                case IdentifierNameSyntax identifier:
                    {
                        var member = GetAssignMember(type, identifier.Identifier.Text);
                        if (member == null)
                            return ToError(item, "Member not found.");

                        target = Expression.MakeMemberAccess(instanceVariable, member);
                        memberType = member is PropertyInfo pi ? pi.PropertyType : ((FieldInfo)member).FieldType;
                        break;
                    }

                default:
                    return ToError(item);
            }

            _targetType = memberType;
            var value = Visit(ae.Right);
            _targetType = previousTargetType;

            if (value == null)
                return null;

            if (!EnsureArgumentType(memberType, ref value))
                return ToError(ae.Right, $"Cannot convert initializer value to '{memberType.GetFriendlyTypeName()}'.");

            statements.Add(Expression.Assign(target, value));
        }

        statements.Add(instanceVariable);

        return Expression.Block([instanceVariable], statements);
    }
    private Expression? CreateCollectionInitializer(Expression instance, Type type, InitializerExpressionSyntax initializer)
    {
        var previousTargetType = _targetType;
        var elementType = type.IsGenericType && type.GetGenericArguments().Length == 1 ? type.GetGenericArguments()[0] : null;
        var addMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance).ToListFast(x => x.Name == "Add");

        var instanceVariable = Expression.Variable(type, "collection");
        var statements = new List<Expression> { Expression.Assign(instanceVariable, instance) };

        foreach (var item in initializer.Expressions)
        {
            IList<Expression> addArgs;

            if (item is InitializerExpressionSyntax nested)
            {
                _targetType = null;

                var nestedExpressions = nested.Expressions;
                var args = new Expression[nestedExpressions.Count];

                for (var i = 0; i < args.Length; i++)
                {
                    var expression = Visit(nestedExpressions[i]);
                    if (expression == null)
                    {
                        _targetType = previousTargetType;
                        return null;
                    }

                    args[i] = expression;
                }

                addArgs = args;
            }
            else
            {
                _targetType = elementType;

                var expression = Visit(item);
                if (expression == null)
                {
                    _targetType = previousTargetType;
                    return null;
                }

                addArgs = [expression];
            }

            _targetType = previousTargetType;

            if (!TryResolveMethodCall(item, instanceVariable, addArgs, addMethods, out var addCall))
                return ToError(item, $"No suitable 'Add' method found on '{type.GetFriendlyTypeName()}'.");
            if (addCall == null)
                return null;

            statements.Add(addCall);
        }

        statements.Add(instanceVariable);

        return Expression.Block([instanceVariable], statements);
    }
    private static Expression CreateMultiDimArray(Type elementType, int rank, IList<int> dimensions, IList<Expression> elements)
    {
        var arrayType = elementType.MakeArrayType(rank);
        var arrayVariable = Expression.Variable(arrayType, "array");

        var statements = new List<Expression>(elements.Count + 2)
        {
            Expression.Assign(arrayVariable, Expression.NewArrayBounds(elementType, dimensions.ToFastArray(x => (Expression)Expression.Constant(x))))
        };

        // Convert the flat indexes i into a per-dimension indexes (row-major order)
        for (var i = 0; i < elements.Count; i++)
        {
            var indices = new Expression[rank];
            var remainder = i;
            for (var d = rank - 1; d >= 0; d--)
            {
                indices[d] = Expression.Constant(remainder % dimensions[d]);
                remainder /= dimensions[d];
            }

            statements.Add(Expression.Assign(Expression.ArrayAccess(arrayVariable, indices), elements[i]));
        }

        statements.Add(arrayVariable);

        return Expression.Block([arrayVariable], statements);
    }

    private Expression? ResolveDelayNew(DelayNewExpression delayNew, Type targetType)
    {
        return CreateObject(delayNew.Node, targetType, delayNew.Node.ArgumentList, delayNew.Node.Initializer);
    }

    private MemberInfo? GetAssignMember(Type type, string name)
    {
        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | _context.CaseInsensitiveFlag;

        for (; type != null!; type = type.BaseType!)
        {
            var member = (MemberInfo?)type.GetProperty(name, flags) ?? type.GetField(name, flags);
            if (member != null)
                return member;
        }

        return null;
    }
}
