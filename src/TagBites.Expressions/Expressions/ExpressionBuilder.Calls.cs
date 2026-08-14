using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TagBites.Expressions.Extensions;
using TagBites.Utils;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    public override Expression? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // nameof(...) operator
        if (node is { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" }, ArgumentList.Arguments.Count: 1 }
            && !IsKnownIdentifier("nameof")
            && TryGetNameOfValue(node.ArgumentList.Arguments[0].Expression) is { } nameOfValue)
        {
            return Expression.Constant(nameOfValue);
        }

        // Parameters
        var parameters = ResolveParameters(node.ArgumentList.Arguments);
        if (parameters == null)
            return null;

        var argumentNames = GetArgumentNames(node.ArgumentList.Arguments);

        // Find instance, type, name syntax
        Expression? instanceExpression;
        Type? instanceType;
        SimpleNameSyntax? methodNameSyntax;
        string methodName;

        switch (node.Expression)
        {
            case IdentifierNameSyntax ins:
                {
                    methodNameSyntax = ins;

                    // Parameter as method
                    var name = methodNameSyntax.Identifier.ValueText;
                    var parameter = FindParameter(name);
                    if (parameter != null && typeof(Delegate).IsAssignableFrom(parameter.Type))
                    {
                        instanceExpression = parameter;
                        methodNameSyntax = null;
                        methodName = "Invoke";
                    }
                    // Member as method
                    else if (_context.GlobalMembers?.TryGetValue(name, out var item) == true
                             && GetGlobalMemberType(name, item) is var memberType
                             && typeof(Delegate).IsAssignableFrom(memberType))
                    {
                        instanceExpression = Expression.Constant(item.Value, memberType);
                        methodNameSyntax = null;
                        methodName = "Invoke";
                    }
                    // Custom operator or 'this' method
                    else
                    {
                        methodName = methodNameSyntax.Identifier.ValueText;

                        if (_options.AllowRuntimeCast
                            && methodName is "typeis" or "typeas" or "typecast"
                            && ResolveCustomKeywords(node, methodName, parameters) is { } expression)
                        {
                            return expression;
                        }

                        instanceExpression = _context.ThisParameter;
                    }
                }
                break;

            case MemberAccessExpressionSyntax ma:
                {
                    instanceExpression = Visit(ma.Expression);
                    if (instanceExpression == null)
                        return null;

                    methodNameSyntax = ma.Name;
                    methodName = methodNameSyntax.Identifier.ValueText;
                    break;
                }

            case MemberBindingExpressionSyntax mbs:
                {
                    instanceExpression = TryPop();
                    if (instanceExpression == null)
                        return null;

                    methodNameSyntax = mbs.Name;
                    methodName = methodNameSyntax.Identifier.ValueText;
                    break;
                }

            case GenericNameSyntax g:
                {
                    instanceExpression = _context.ThisParameter;
                    methodNameSyntax = g;
                    methodName = methodNameSyntax.Identifier.ValueText;
                    break;
                }

            default:
                return ToError(node);
        }

        switch (instanceExpression)
        {
            // A typeof(...) value keeps Expression.Type == typeof(Type) and is a Type instance, not a static type reference
            case ConstantExpression { Value: Type type } constant when constant.Type != typeof(Type):
                instanceExpression = null;
                instanceType = type;
                break;

            default:
                instanceType = instanceExpression?.Type;
                break;
        }

        // Method name with generics
        Type[]? genericTypes = null;

        if (methodNameSyntax is GenericNameSyntax gns)
        {
            genericTypes = new Type[gns.TypeArgumentList.Arguments.Count];
            for (var i = 0; i < genericTypes.Length; i++)
            {
                var type = ResolveType(gns.TypeArgumentList.Arguments[0]);
                if (type == null)
                    return null;

                genericTypes[i] = type;
            }
        }

        // Custom keywords
        if (instanceType == null)
        {
            // Static import
            if (_context.StaticImports?.Count > 0 && TryResolveStaticImportCall(node, methodName, genericTypes, parameters, out var staticExpression, argumentNames))
                return staticExpression;

            // Statics methods that every type inherits from object: Equals, ReferenceEquals
            var objectMethods = GetMethods(typeof(object), methodName, BindingFlags.Static);
            if (objectMethods.Count > 0 && TryResolveMethodCall(node, null, parameters, objectMethods, out var objectStatic, argumentNames))
                return objectStatic;

            if (FirstError != null)
                return null;

            return ToError(node, $"Method '{methodName}' not found for this arguments.");
        }

        // Custom member as delegate
        if (instanceExpression != null && ResolveCustomMember(instanceExpression, methodName) is { } customMember && typeof(Delegate).IsAssignableFrom(customMember.Type))
        {
            var invokeMethod = customMember.Type.GetMethod("Invoke")!;
            if (invokeMethod.GetParameters().Length != parameters.Count)
                return ToError(node, $"Delegate '{methodName}' does not take {parameters.Count} arguments.");

            return PropagateElementTypeInfo(instanceExpression, Expression.Invoke(customMember, parameters));
        }

        // Instance or static method
        {
            var methods = GetMethods(instanceType, methodName, instanceExpression == null ? BindingFlags.Static : BindingFlags.Instance);
            if (methods.Count > 0)
            {
                if (genericTypes != null)
                {
                    methods = methods.ToListFast(
                        x => x.IsGenericMethodDefinition && x.GetGenericArguments().Length == genericTypes.Length,
                        x => x.MakeGenericMethod(genericTypes));
                }

                if (TryResolveMethodCall(node, instanceExpression, parameters, methods, out var expression, argumentNames))
                    return instanceExpression != null && expression != null ? PropagateElementTypeInfo(instanceExpression, expression) : expression;
            }
        }

        // Extension methods
        {
            // Select method override
            var methods = GetExtensionMethods(instanceType, methodName);
            if (methods.Count > 0)
            {
                if (genericTypes != null)
                {
                    methods = methods.ToListFast(
                        x => x.IsGenericMethodDefinition && x.GetGenericArguments().Length == genericTypes.Length,
                        x => x.MakeGenericMethod(genericTypes));
                }

                var extendedParameters = new List<Expression>(parameters.Count + 1) { instanceExpression! };
                extendedParameters.AddRange(parameters);
                parameters = extendedParameters;

                // The instance becomes the first (positional) argument, so shift the names to match
                var extensionArgumentNames = argumentNames;
                if (argumentNames != null)
                {
                    extensionArgumentNames = new string?[argumentNames.Length + 1];
                    argumentNames.CopyTo(extensionArgumentNames, 1);
                }

                var oldExtensionInstance = _extensionInstance;
                _extensionInstance = instanceExpression;
                try
                {
                    if (TryResolveMethodCall(node, null, parameters, methods, out var expression, extensionArgumentNames))
                        return expression != null ? PropagateElementTypeInfo(instanceExpression!, expression) : null;
                }
                finally
                {
                    _extensionInstance = oldExtensionInstance;
                }
            }
        }

        // Member as delegate
        if (instanceExpression != null && ResolveMember(node, instanceExpression, methodName, setErrorWhenNotFound: false) is { } member && typeof(Delegate).IsAssignableFrom(member.Type))
        {
            var invokeMethod = member.Type.GetMethod("Invoke")!;
            if (invokeMethod.GetParameters().Length != parameters.Count)
                return ToError(node, $"Delegate '{methodName}' does not take {parameters.Count} arguments.");

            return PropagateElementTypeInfo(instanceExpression, Expression.Invoke(member, parameters));
        }

        // Static import
        if (_context.StaticImports?.Count > 0
            && node.Expression is IdentifierNameSyntax or GenericNameSyntax
            && TryResolveStaticImportCall(node, methodName, genericTypes, parameters, out var staticImportExpression, argumentNames))
        {
            return staticImportExpression;
        }

        return ToError(node, $"Method '{methodName}' not found for this arguments.");
    }
    public override Expression? VisitArgument(ArgumentSyntax node) => Visit(node.Expression);

    private IList<Expression>? ResolveParameters(SeparatedSyntaxList<ArgumentSyntax> node)
    {
        var count = node.Count;
        if (count == 0)
            return Array.Empty<Expression>();

        var parameters = new Expression[count];

        for (var i = 0; i < count; i++)
        {
            var argExpression = Visit(node[i]);
            if (argExpression == null)
                return null;

            parameters[i] = argExpression;
        }

        return parameters;
    }
    private static string?[]? GetArgumentNames(SeparatedSyntaxList<ArgumentSyntax> node)
    {
        string?[]? names = null;

        for (var i = 0; i < node.Count; i++)
        {
            var name = node[i].NameColon?.Name.Identifier.ValueText;
            if (name == null)
                continue;

            names ??= new string?[node.Count];
            names[i] = name;
        }

        return names;
    }

    private bool TryResolveStaticImportCall(SyntaxNode node, string methodName, Type[]? genericTypes, IList<Expression> parameters, out Expression? expression, IReadOnlyList<string?>? argumentNames = null)
    {
        expression = null;

        foreach (var import in _context.StaticImports!.Values)
        {
            var methods = GetMethods(import, methodName, BindingFlags.Static);
            if (methods.Count == 0)
                continue;

            if (genericTypes != null)
            {
                methods = methods.ToListFast(
                    x => x.IsGenericMethodDefinition && x.GetGenericArguments().Length == genericTypes.Length,
                    x => x.MakeGenericMethod(genericTypes));
            }

            if (TryResolveMethodCall(node, null, parameters, methods, out expression, argumentNames))
                return true;
        }

        return false;
    }

    private Expression? ResolveCustomKeywords(SyntaxNode node, string methodName, IList<Expression> parameters)
    {
        // ReSharper disable StringLiteralTypo
        if (parameters.Count != 2 || parameters[1] is not ConstantExpression { Value: string typeName })
            return null;

        var runtimeType = Type.GetType(typeName);
        if (runtimeType == null)
            return ToError(node, $"Runtime type '{typeName}' not found.");

        var expression = parameters[0];

        return methodName switch
        {
            "typeis" => ToIsOperator(expression, Expression.Constant(runtimeType)),
            "typeas" => ToAsOperator(node, expression, Expression.Constant(runtimeType)),
            "typecast" => ToCastOperator(node, expression, Expression.Constant(runtimeType), false),
            _ => throw new ArgumentOutOfRangeException()
        };
        // ReSharper restore StringLiteralTypo
    }
    private static string? TryGetNameOfValue(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            GenericNameSyntax g => g.Identifier.ValueText,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            _ => null
        };
    }

    private bool TryResolveMethodCall(SyntaxNode relatedNode, Expression? instanceExpression, IList<Expression> arguments, IList<MethodInfo> candidates, out Expression? expression, IReadOnlyList<string?>? argumentNames = null)
    {
        return TryResolveCall(relatedNode, instanceExpression, arguments, candidates, out expression, argumentNames);
    }
    private bool TryResolveCall<T>(SyntaxNode relatedNode, Expression? instanceExpression, IList<Expression> arguments, IList<T> candidates, out Expression? expression, IReadOnlyList<string?>? argumentNames = null) where T : MethodBase
    {
        expression = null;

        // Select best method
        MethodCallInfo? bestMethod = null;
        List<MethodCallInfo>? ambiguous = null;
        List<(DelayLambdaExpression Node, Type[] Parameters, Expression Lambda)>? reusableLambdas = null;

        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < candidates.Count; i++)
        {
            var info = ToMatchingMethod(candidates[i]);
            if (info == null || !HasMatchingParameters(info.Parameters, info.Arguments))
                continue;

            if (bestMethod == null)
            {
                bestMethod = info;
                continue;
            }

            var newBestMethod = GetBestMatchingMethod(bestMethod, info);
            if (newBestMethod == null)
            {
                ambiguous ??= [];
                ambiguous.Add(info);
                ambiguous.Add(bestMethod);
            }

            bestMethod = newBestMethod;
        }

        if (ambiguous?.Count > 0)
        {
            if (bestMethod == null || ambiguous.Any(x => GetBestMatchingMethod(bestMethod, x) != bestMethod))
            {
                expression = ToError(relatedNode, "Ambiguous method call.");
                return true;
            }
        }

        if (bestMethod == null)
            return false;

        if (bestMethod.Method is ConstructorInfo constructor)
        {
            expression = Expression.New(constructor, bestMethod.Arguments);
            return true;
        }

        var method0 = (MethodInfo)bestMethod.Method;
        expression = Expression.Call(instanceExpression, method0, bestMethod.Arguments);

        var resultShape = _tupleShapes != null
            ? ValueTupleShape.ComputeCallResultShape(method0, instanceExpression, bestMethod.Arguments, GetTupleShape, _nameComparison)
            : null;
        SetTupleShape(expression, resultShape ?? ValueTupleShape.FromMethodReturn(method0));
        return true;

        MethodCallInfo? ToMatchingMethod(MethodBase x)
        {
            var methodParameters = x.GetParameters();
            var hasParams = methodParameters.Length > 0 && methodParameters[methodParameters.Length - 1].IsDefined(typeof(ParamArrayAttribute), false);

            // Reorder named arguments into declared parameter order, filling omitted optional parameters with their default values.
            // arguments stays in source order (RawArguments) so overload resolution can compare candidates by the argument the caller actually wrote;
            // argumentMap records where each source argument landed for that comparison.
            var effectiveArguments = arguments;
            int[]? argumentMap = null;
            if (argumentNames != null)
            {
                var bound = TryBindNamedArguments(arguments, argumentNames, methodParameters, hasParams, out argumentMap);
                if (bound == null)
                    return null;

                effectiveArguments = bound;
            }

            if (!hasParams)
            {
                // A params method accepts more arguments than declared parameters.
                if (methodParameters.Length < effectiveArguments.Count)
                    return null;

                // Too few arguments, reject if any unfilled parameter is required (no default value).
                for (var i = effectiveArguments.Count; i < methodParameters.Length; i++)
                    if (!methodParameters[i].HasDefaultValue)
                        return null;
            }

            // Build method info
            var info = new MethodCallInfo
            {
                Method = x,
                RawArguments = arguments,
                Arguments = effectiveArguments.ToList(),
                Parameters = methodParameters,
                HasParams = hasParams,
                ArgumentMap = argumentMap
            };
            ref var method = ref info.Method;
            var methodArguments = info.Arguments;

            // Try extract arguments
            Type[]? genericParameters = null;
            Type?[]? genericArguments = null;

            if (method.IsGenericMethodDefinition)
            {
                genericParameters = method.GetGenericArguments();
                genericArguments = new Type[genericParameters.Length];

                var argumentTypes = new List<(string, Type)>();

                for (var i = 0; i < methodArguments.Count; i++)
                    if (methodArguments[i] is not DelayLambdaExpression and not DelayDefaultExpression and not DelayNewExpression)
                        TryExtractGenericArguments(methodParameters[Math.Min(i, methodParameters.Length - 1)].ParameterType, methodArguments[i].Type, argumentTypes);

                for (var i = 0; i < genericParameters.Length; i++)
                {
                    var typeName = genericParameters[i].Name;
                    var fullType = argumentTypes.FastFirstOrDefault(y => y.Item1 == typeName).Item2;
                    genericArguments[i] = fullType;
                }
            }

            if (method.IsGenericMethodDefinition && genericArguments!.All(y => y != null))
            {
                method = ((MethodInfo)method).MakeGenericMethod(genericArguments!);
                methodParameters = method.GetParameters();
            }

            // Tuple shape flow into lambda parameters (only relevant when named-tuple shapes are being tracked)
            Dictionary<Type, ValueTupleShape?>? lambdaBindings = null;
            MethodBase? openDefinition = _tupleShapes == null
                ? null
                : x.IsGenericMethodDefinition
                    ? x
                    : x.IsGenericMethod
                        ? ((MethodInfo)x).GetGenericMethodDefinition()
                        : null;
            if (openDefinition != null)
            {
                var openParameters = openDefinition.GetParameters();
                var count = Math.Min(methodArguments.Count, openParameters.Length);

                var anyShape = false;
                for (var i = 0; i < count; i++)
                    if (methodArguments[i] is not DelayLambdaExpression and not DelayDefaultExpression and not DelayNewExpression && GetTupleShape(methodArguments[i]) != null)
                    {
                        anyShape = true;
                        break;
                    }

                if (anyShape)
                {
                    lambdaBindings = new Dictionary<Type, ValueTupleShape?>();
                    var bound = new HashSet<Type>();
                    for (var i = 0; i < count; i++)
                        if (methodArguments[i] is not DelayLambdaExpression and not DelayDefaultExpression and not DelayNewExpression)
                            ValueTupleShape.BindShape(openParameters[i].ParameterType, GetTupleShape(methodArguments[i]), lambdaBindings, bound, !typeof(Delegate).IsAssignableFrom(methodArguments[i].Type), _nameComparison, 0);
                }
            }

            // Parse lambda
            for (var i = 0; i < methodArguments.Count; i++)
                if (methodArguments[i] is DelayLambdaExpression dl)
                {
                    // Close already-known generic arguments in the delegate type
                    var lambdaType = methodParameters[Math.Min(i, methodParameters.Length - 1)].ParameterType;
                    if (genericParameters != null)
                        lambdaType = SubstituteGenericArguments(lambdaType, genericParameters, genericArguments!);

                    // Parameter types from the delegate's Invoke, so any delegate shape works
                    if (lambdaType.GetMethod("Invoke") is not { } invoke)
                        return null;

                    var lambdaParameters = invoke.GetParameters().ToFastArray(p => p.ParameterType);

                    // A lambda parameter type that still carries an unbound type argument cannot be used to build the lambda.
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    foreach (var lambdaParameter in lambdaParameters)
                        if (lambdaParameter.ContainsGenericParameters)
                            return null;

                    // Shapes for the lambda parameters, read from the bindings by matching the open delegate's generic arguments
                    // (e.g. Func<TSource, bool> -> TSource) against this method's bindings.
                    ValueTupleShape?[]? lambdaParameterShapes = null;

                    if (lambdaBindings != null && openDefinition != null)
                    {
                        var openParameters = openDefinition.GetParameters();
                        var openDelegateType = openParameters[Math.Min(i, openParameters.Length - 1)].ParameterType;
                        if (openDelegateType.IsGenericType)
                        {
                            var openDelegateArgs = openDelegateType.GetGenericArguments();
                            for (var j = 0; j < lambdaParameters.Length && j < openDelegateArgs.Length; j++)
                                if (openDelegateArgs[j].IsGenericParameter && lambdaBindings.TryGetValue(openDelegateArgs[j], out var parameterShape) && parameterShape != null)
                                {
                                    lambdaParameterShapes ??= new ValueTupleShape?[lambdaParameters.Length];
                                    lambdaParameterShapes[j] = parameterShape;
                                }
                        }
                    }

                    // Build only non-Func/Action delegates explicitly; Func/Action stay inferred to keep the C# overload preference
                    var buildAsDelegate = !lambdaType.ContainsGenericParameters
                        && !lambdaType.Name.StartsWith("Func`", StringComparison.Ordinal)
                        && !lambdaType.Name.StartsWith("Action`", StringComparison.Ordinal);

                    // Reuse a body already built for the same parameter types by another overload candidate
                    Expression? expression = null;
                    var reusable = !buildAsDelegate && lambdaParameterShapes == null;

                    if (reusable && reusableLambdas != null)
                        foreach (var item in reusableLambdas)
                            if (item.Node == dl && AreTypesEqual(item.Parameters, lambdaParameters))
                            {
                                expression = item.Lambda;
                                break;
                            }

                    if (expression == null)
                    {
                        expression = TryResolveLambda(dl.Node, lambdaParameters, lambdaParameterShapes, buildAsDelegate ? lambdaType : null, lambdaType);
                        if (expression == null)
                            return null;

                        if (reusable)
                            (reusableLambdas ??= []).Add((dl, lambdaParameters, expression));
                    }

                    methodArguments[i] = expression;

                    // Infer generics by matching the delegate Invoke signatures, so a generic return works across delegate families
                    if (method.IsGenericMethodDefinition)
                    {
                        var argumentTypes = new List<(string, Type)>();
                        if (methodParameters[i].ParameterType.GetMethod("Invoke") is { } openInvoke && methodArguments[i].Type.GetMethod("Invoke") is { } builtInvoke)
                        {
                            var openInvokeParameters = openInvoke.GetParameters();
                            var builtInvokeParameters = builtInvoke.GetParameters();

                            for (var k = 0; k < openInvokeParameters.Length && k < builtInvokeParameters.Length; k++)
                                TryExtractGenericArguments(openInvokeParameters[k].ParameterType, builtInvokeParameters[k].ParameterType, argumentTypes);

                            TryExtractGenericArguments(openInvoke.ReturnType, builtInvoke.ReturnType, argumentTypes);
                        }

                        for (var j = 0; j < genericArguments!.Length; j++)
                        {
                            if (genericArguments[j] != null)
                                continue;

                            var typeName = genericParameters![j].Name;
                            var fullType = argumentTypes.FastFirstOrDefault(y => y.Item1 == typeName).Item2;
                            genericArguments[j] = fullType;
                        }
                    }
                }


            // Make
            if (method.IsGenericMethodDefinition)
            {
                if (genericArguments!.Any(y => y == null))
                    return null;

                method = ((MethodInfo)method).MakeGenericMethod(genericArguments!);
                methodParameters = method.GetParameters();
            }

            // Resolve defaults
            for (var i = methodArguments.Count - 1; i >= 0; i--)
                if (methodArguments[i] is DelayDefaultExpression)
                {
                    var targetType = info.HasParams && i >= methodParameters.Length - 1 && methodArguments.Count != methodParameters.Length
                        ? methodParameters[methodParameters.Length - 1].ParameterType.GetElementType()!
                        : methodParameters[Math.Min(i, methodParameters.Length - 1)].ParameterType;
                    methodArguments[i] = Expression.Default(targetType);
                }

            // Resolve target-typed new() against the now-known parameter type
            for (var i = 0; i < methodArguments.Count; i++)
                if (methodArguments[i] is DelayNewExpression dn)
                {
                    var targetType = info.HasParams && i >= methodParameters.Length - 1 && methodArguments.Count != methodParameters.Length
                        ? methodParameters[methodParameters.Length - 1].ParameterType.GetElementType()!
                        : methodParameters[Math.Min(i, methodParameters.Length - 1)].ParameterType;

                    var created = ResolveDelayNew(dn, targetType);
                    if (created == null)
                        return null;

                    methodArguments[i] = created;
                }

            // Params array: pass a matching array directly (normal form) or collect the trailing arguments into a new array,
            // converting each to the element type (expanded form).
            if (info.HasParams)
            {
                var paramsIndex = methodParameters.Length - 1;
                var paramsArrayType = methodParameters[paramsIndex].ParameterType;
                var normalForm = methodArguments.Count == methodParameters.Length
                                 && IsMatchingParameterType(paramsArrayType, methodArguments[paramsIndex].Type);

                if (!normalForm)
                {
                    if (methodArguments.Count < paramsIndex || !paramsArrayType.IsArray)
                        return null;

                    var elementType = paramsArrayType.GetElementType()!;
                    var elements = new Expression[methodArguments.Count - paramsIndex];

                    for (var i = paramsIndex; i < methodArguments.Count; i++)
                    {
                        var element = methodArguments[i];
                        if (!EnsureArgumentType(elementType, ref element))
                            return null;

                        elements[i - paramsIndex] = element;
                    }

                    methodArguments.RemoveRange(paramsIndex, methodArguments.Count - paramsIndex);
                    methodArguments.Add(Expression.NewArrayInit(elementType, elements));
                }
            }

            // Cast method arguments
            for (var i = 0; i < methodArguments.Count; i++)
            {
                var parameterType = methodParameters[i].ParameterType;
                var argumentType = methodArguments[i].Type;

                if (parameterType != argumentType)
                {
                    // Rebind a Func/Action lambda to a signature-compatible delegate parameter
                    if (methodArguments[i] is LambdaExpression lambdaArgument && TryRebindLambdaToDelegate(lambdaArgument, parameterType) is { } rebound)
                    {
                        methodArguments[i] = rebound;
                        continue;
                    }

                    var argument = methodArguments[i];
                    if (!EnsureArgumentType(parameterType, ref argument))
                        return null;

                    methodArguments[i] = argument;
                }
            }

            // Default arguments
            for (var i = methodArguments.Count; i < methodParameters.Length; i++)
            {
                if (!methodParameters[i].HasDefaultValue)
                    return null;

                var defaultValue = methodParameters[i].DefaultValue;
                var parameterType = methodParameters[i].ParameterType;

                if (defaultValue == null && parameterType.IsValueType)
                    defaultValue = Activator.CreateInstance(parameterType);

                methodArguments.Add(Expression.Constant(defaultValue, parameterType));
            }

            // Publish the final, closed parameters.
            // Overload betterness reads info.Parameters, so it must see the closed types - otherwise a generic IEnumerable<T> overload loses to a params object[] one.
            info.Parameters = methodParameters;

            return info;
        }
    }

    private List<Expression>? TryBindNamedArguments(IList<Expression> arguments, IReadOnlyList<string?> names, ParameterInfo[] methodParameters, bool hasParams, out int[]? argumentMap)
    {
        argumentMap = null;

        var outOfPositionNamed = false; // Once a named argument lands out of position, no positional argument may follow it.
        var fixedCount = hasParams
            ? methodParameters.Length - 1
            : methodParameters.Length;
        var slots = new Expression?[fixedCount];
        var filled = new bool[fixedCount];
        var map = new int[arguments.Count];
        List<Expression>? paramsTail = null;

        for (var i = 0; i < arguments.Count; i++)
        {
            var name = i < names.Count ? names[i] : null;

            if (name == null)
            {
                if (outOfPositionNamed)
                    return null;

                if (i < fixedCount)
                {
                    if (filled[i])
                    {
                        // Slot already taken by a named argument.
                        return null;
                    }

                    slots[i] = arguments[i];
                    filled[i] = true;
                    map[i] = i;
                }
                else if (hasParams)
                {
                    (paramsTail ??= []).Add(arguments[i]);
                    map[i] = fixedCount;
                }
                else
                {
                    // Too many positional arguments
                    return null;
                }
            }
            else
            {
                // Named arguments cannot target the params array
                var index = -1;
                for (var p = 0; p < fixedCount; p++)
                    if (string.Equals(methodParameters[p].Name, name, _nameComparison))
                    {
                        index = p;
                        break;
                    }

                if (index < 0 || filled[index])
                {
                    // Unknown name, duplicate, or params-array target.
                    return null;
                }

                slots[index] = arguments[i];
                filled[index] = true;
                map[i] = index;

                if (index != i)
                    outOfPositionNamed = true;
            }
        }

        var result = new List<Expression>(methodParameters.Length);

        for (var p = 0; p < fixedCount; p++)
        {
            if (filled[p])
            {
                result.Add(slots[p]!);
                continue;
            }

            var parameter = methodParameters[p];
            if (!parameter.HasDefaultValue)
            {
                // Required parameter left unfilled
                return null;
            }

            result.Add(CreateDefaultArgument(parameter));
        }

        if (paramsTail != null)
            result.AddRange(paramsTail);

        argumentMap = map;
        return result;
    }
    private bool EnsureArgumentType(Type parameterType, ref Expression argument)
    {
        if ((TryConvertExpression(argument, parameterType) ?? TryConvertConstant(argument, parameterType)) is not { } converted)
            return false;

        // A conversion makes a new expression; carry the shape across so names keep flowing,
        // e.g. OrderBy().First() converts IOrderedEnumerable<T> to IEnumerable<T>.
        if (!ReferenceEquals(converted, argument))
            SetTupleShape(converted, GetTupleShape(argument));

        argument = converted;
        return true;
    }

    private static bool HasMatchingParameters(IList<ParameterInfo> parameters, IList<Expression> arguments)
    {
        if (parameters.Count < arguments.Count)
            return false;

        for (var i = 0; i < arguments.Count; i++)
        {
            var mpt = parameters[i].ParameterType;

            // The null literal binds to any reference or nullable parameter, but not to a non-nullable value type.
            if (IsNullLiteral(arguments[i]))
            {
                if (mpt.IsValueType && !IsNullableType(mpt))
                    return false;

                continue;
            }

            if (IsMatchingParameterType(mpt, arguments[i].Type))
                continue;

            if (TryConvertConstant(arguments[i], mpt) != null)
                continue;

            if (arguments[i] is LambdaExpression lambdaArgument && TryRebindLambdaToDelegate(lambdaArgument, mpt) != null)
                continue;

            return false;
        }

        return true;
    }
    private static bool IsMatchingParameterType(Type parameterType, Type argumentType)
    {
        if (parameterType.IsAssignableFrom(argumentType))
        {
            // C# does not allow enum-array covariance, e.g. DayOfWeek[] -> IEnumerable<int>,
            // reject it so overload resolution keeps the enum element type (DayOfWeek[].Max() -> DayOfWeek).
            return !argumentType.IsArray
                   || argumentType.GetElementType() is not { IsEnum: true } enumElement
                   || GetSequenceElementType(parameterType) is not { } target
                   || target == enumElement
                   || target != Enum.GetUnderlyingType(enumElement);
        }

        // Numeric mismatch
        if (TypeUtils.IsNumericType(argumentType) && TypeUtils.IsNumericType(parameterType))
            return TypeUtils.HasImplicitNumericConversion(argumentType, parameterType);

        // User-defined implicit conversion
        if (FindConversionOperator(argumentType, parameterType, "op_Implicit") != null)
            return true;

        // Generic
        return TryExtractGenericArguments(parameterType, argumentType, null);
    }

    private static MethodCallInfo? GetBestMatchingMethod(MethodCallInfo method1, MethodCallInfo method2)
    {
        // Both candidates get the same source-order args. If named arguments were used, each candidate's ArgumentMap tells which parameter a given arg was bound to.
        var args = method1.RawArguments;

        for (var i = 0; i < args.Count; i++)
        {
            // A 'default' matches any parameter type
            if (args[i] is DelayDefaultExpression)
                continue;

            var p1 = method1.ArgumentMap?[i] ?? (method1.HasParams ? Math.Min(i, method1.Parameters.Length - 1) : i);
            var p2 = method2.ArgumentMap?[i] ?? (method2.HasParams ? Math.Min(i, method2.Parameters.Length - 1) : i);
            var t1 = method1.Parameters[p1].ParameterType;
            var t2 = method2.Parameters[p2].ParameterType;

            // A lambda prefers the candidate with the narrower delegate return, e.g. Sum's Func<T, int> over Func<T, long>
            if (args[i] is DelayLambdaExpression)
            {
                if (t1 != t2 && t1.GetMethod("Invoke")?.ReturnType is { } r1 && t2.GetMethod("Invoke")?.ReturnType is { } r2 && r1 != r2)
                {
                    if (Nullable.GetUnderlyingType(r2) == r1)
                        return method1;

                    if (Nullable.GetUnderlyingType(r1) == r2)
                        return method2;

                    var m1 = IsMatchingParameterType(r2, r1);
                    var m2 = IsMatchingParameterType(r1, r2);

                    if (m1 && !m2)
                        return method1;

                    if (m2 && !m1)
                        return method2;
                }

                continue;
            }

            // The null literal and target-typed new() have no source type, but still prefer the more specific (more derived) parameter type
            if (IsNullLiteral(args[i]) || args[i] is DelayNewExpression)
            {
                if (t1 != t2)
                {
                    if (t2.IsAssignableFrom(t1))
                        return method1;

                    if (t1.IsAssignableFrom(t2))
                        return method2;
                }

                continue;
            }

            // Expanded params compares by the element type, e.g. Split(params char[]) vs Split(char, int)
            if (method1.HasParams && p1 == method1.Parameters.Length - 1 && t1.IsArray && args[i].Type != t1)
                t1 = t1.GetElementType()!;

            if (method2.HasParams && p2 == method2.Parameters.Length - 1 && t2.IsArray && args[i].Type != t2)
                t2 = t2.GetElementType()!;

            if (GetBestMatchingType(args[i].Type, t1, t2) is { } best)
                return best == t1 ? method1 : method2;
        }

        if (method1.Method.IsGenericMethod.CompareTo(method2.Method.IsGenericMethod) is var genericDiff && genericDiff != 0)
            return genericDiff < 0 ? method1 : method2;

        // More specific open signature: at a given position a concrete type beats a type parameter.
        // This separates Enumerable.Max<TSource>(..., Func<TSource, int>)
        // from the fully generic Enumerable.Max<TSource, TResult>(..., Func<TSource, TResult>) once their closed parameters match.
        // Only relevant when a generic method is involved; two non-generic candidates never differ in specificity.
        if ((method1.Method.IsGenericMethod || method2.Method.IsGenericMethod)
            && CompareOpenSpecificity(method1.Method, method2.Method) is var specificityDiff && specificityDiff != 0)
            return specificityDiff < 0 ? method1 : method2;

        if (method1.HasParams.CompareTo(method2.HasParams) is var paramsDiff && paramsDiff != 0)
            return paramsDiff < 0 ? method1 : method2;

        if (method1.Parameters.Length.CompareTo(method2.Parameters.Length) is var parametersCountDiff && parametersCountDiff != 0)
            return parametersCountDiff > 0 ? method1 : method2;

        return null;
    }
    private static Type? GetBestMatchingType(Type argType, Type type1, Type type2)
    {
        if (type1 == type2)
            return null;

        if (argType == type1 || argType == type2)
            return argType;

        // More specific type wins, e.g. 1L.Equals(1) takes Equals(long) over Equals(object)
        var m1 = IsMatchingParameterType(type2, type1);
        var m2 = IsMatchingParameterType(type1, type2);

        if (m1 && !m2)
            return type1;
        if (m2 && !m1)
            return type2;

        // Assignable
        var a1 = type1.IsAssignableFrom(argType);
        var a2 = type2.IsAssignableFrom(argType);

        if (a1 && !a2)
            return type1;
        if (a2 && !a1)
            return type2;

        // Sign
        var s1 = IsUnsignedType(type1);
        var s2 = IsUnsignedType(type2);

        if (s1 == false && s2 == true)
            return type1;
        if (s2 == false && s1 == true)
            return type2;

        return null;
    }

    private static int CompareOpenSpecificity(MethodBase method1, MethodBase method2)
    {
        var p1 = GetOpenParameters(method1);
        var p2 = GetOpenParameters(method2);
        if (p1.Length != p2.Length)
            return 0;

        var result = 0;

        for (var i = 0; i < p1.Length; i++)
        {
            var diff = CompareTypeSpecificity(p1[i].ParameterType, p2[i].ParameterType);
            if (diff == 0)
                continue;

            if (result == 0)
                result = diff;
            else if (result != diff)
            {
                // Each is more specific somewhere, so neither wins.
                return 0;
            }
        }

        return result;

        static ParameterInfo[] GetOpenParameters(MethodBase method)
        {
            return method is MethodInfo { IsGenericMethod: true } mi
                ? mi.GetGenericMethodDefinition().GetParameters()
                : method.GetParameters();
        }
    }
    private static int CompareTypeSpecificity(Type type1, Type type2)
    {
        if (type1 == type2)
            return 0;

        // A type parameter is less specific than any concrete type.
        if (type1.IsGenericParameter || type2.IsGenericParameter)
        {
            return type1.IsGenericParameter != type2.IsGenericParameter
                ? type1.IsGenericParameter ? 1 : -1
                : 0;
        }

        if (type1.IsArray && type2.IsArray)
        {
            // ReSharper disable once TailRecursiveCall
            return CompareTypeSpecificity(type1.GetElementType()!, type2.GetElementType()!);
        }

        if (type1.IsGenericType && type2.IsGenericType)
        {
            var a1 = type1.GetGenericArguments();
            var a2 = type2.GetGenericArguments();
            if (a1.Length != a2.Length)
                return 0;

            var result = 0;
            for (var i = 0; i < a1.Length; i++)
            {
                var diff = CompareTypeSpecificity(a1[i], a2[i]);
                if (diff == 0)
                    continue;

                if (result == 0)
                    result = diff;
                else if (result != diff)
                    return 0;
            }

            return result;
        }

        return 0;
    }

    private static bool TryExtractGenericArguments(Type parameterWithGeneric, Type argumentType, IList<(string, Type)>? argumentTypes)
    {
        if (parameterWithGeneric.IsGenericParameter)
        {
            argumentTypes?.Add((parameterWithGeneric.Name, argumentType));
            return true;
        }

        // Element-wise for arrays, e.g. T from T[] in Array.Exists(new[] { 1, 2 }, x => x > 1)
        if (parameterWithGeneric.IsArray)
            return argumentType.IsArray
                   && parameterWithGeneric.GetArrayRank() == argumentType.GetArrayRank()
                   && TryExtractGenericArguments(parameterWithGeneric.GetElementType()!, argumentType.GetElementType()!, argumentTypes);

        if (!parameterWithGeneric.IsGenericType)
            return parameterWithGeneric == argumentType;

        if (parameterWithGeneric.IsInterface)
        {
            var definition = parameterWithGeneric.IsGenericTypeDefinition
                ? parameterWithGeneric
                : parameterWithGeneric.GetGenericTypeDefinition();

            var argumentIsDefinition = argumentType.IsGenericType && argumentType.GetGenericTypeDefinition() == definition;
            if (argumentType != definition && !argumentIsDefinition)
            {
                var interfaceType = argumentType.GetInterfaces().FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == definition);
                if (interfaceType == null)
                    return false;

                argumentType = interfaceType;
            }
        }
        else if (parameterWithGeneric.IsClass)
        {
            while (argumentType.Name != parameterWithGeneric.Name && argumentType != typeof(object))
                argumentType = argumentType.BaseType!;

            if (argumentType == typeof(object))
                return false;
        }

        if (parameterWithGeneric.Name != argumentType.Name)
            return argumentType.BaseType is { } baseType && TryExtractGenericArguments(parameterWithGeneric, baseType, argumentTypes);

        var pp = parameterWithGeneric.GetGenericArguments();
        var ap = argumentType.GetGenericArguments();
        var result = true;

        for (var i = 0; i < pp.Length; i++)
            result &= TryExtractGenericArguments(pp[i], ap[i], argumentTypes);

        return result;
    }
    private static Type SubstituteGenericArguments(Type type, Type[] genericParameters, Type?[] genericArguments)
    {
        if (type.IsGenericParameter)
        {
            for (var i = 0; i < genericParameters.Length; i++)
                if (genericParameters[i].Name == type.Name)
                    return genericArguments[i] ?? type;

            return type;
        }

        if (type.IsArray)
        {
            var element = SubstituteGenericArguments(type.GetElementType()!, genericParameters, genericArguments);
            if (element == type.GetElementType())
                return type;

            var rank = type.GetArrayRank();
            return rank == 1 ? element.MakeArrayType() : element.MakeArrayType(rank);
        }

        if (type is { IsGenericType: true, ContainsGenericParameters: true })
        {
            var args = type.GetGenericArguments();
            var changed = false;

            for (var i = 0; i < args.Length; i++)
            {
                var substituted = SubstituteGenericArguments(args[i], genericParameters, genericArguments);
                if (substituted != args[i])
                {
                    args[i] = substituted;
                    changed = true;
                }
            }

            // Build the partially-closed type even if some arguments stay open
            if (changed)
                return type.GetGenericTypeDefinition().MakeGenericType(args);
        }

        return type;
    }

    private static Type? GetSequenceElementType(Type type)
    {
        return type.IsArray
            ? type.GetElementType()
            : type.IsGenericType && type.GetGenericArguments().Length == 1 ? type.GetGenericArguments()[0] : null;
    }
    private static bool? IsUnsignedType(Type type)
    {
        var code = Type.GetTypeCode(type);
        return code is >= TypeCode.Byte and <= TypeCode.UInt64
            ? code is TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
            : null;
    }

    private class MethodCallInfo
    {
        public MethodBase Method = null!;

        public ParameterInfo[] Parameters { get; set; } = null!;
        public bool HasParams { get; set; }

        public IList<Expression> RawArguments { get; set; } = null!;
        public List<Expression> Arguments { get; set; } = null!;

        /// <summary>
        /// Maps each source (RawArguments) index to the parameter it binds to.
        /// Null when arguments are positional.
        /// </summary>
        public int[]? ArgumentMap { get; set; }


        public override string ToString() => Method.ToString();
    }
}
