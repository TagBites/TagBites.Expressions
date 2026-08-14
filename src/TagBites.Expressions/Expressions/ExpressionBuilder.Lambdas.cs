using System.Linq.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TagBites.Expressions.Extensions;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    public override Expression VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => new DelayLambdaExpression(node);
    public override Expression VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => new DelayLambdaExpression(node);

    private Expression? TryResolveLambda(LambdaExpressionSyntax node, Type[] parameterTypes, ValueTupleShape?[]? parameterShapes = null, Type? delegateType = null, Type? throwReturnSource = null)
    {
        var simple = node as SimpleLambdaExpressionSyntax;
        var parenthesized = node as ParenthesizedLambdaExpressionSyntax;
        var parameterCount = simple != null ? 1 : parenthesized!.ParameterList.Parameters.Count;
        if (parameterCount != parameterTypes.Length)
            return null;

        var nestedParametersStartIndex = -1;
        var nestedVariableStartIndex = -1;
        ParameterExpression[]? parameters = null;

        _nestedParameters ??= [];
        try
        {
            // Parameters
            parameters = new ParameterExpression[parameterTypes.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var name = (simple != null ? simple.Parameter : parenthesized!.ParameterList.Parameters[i]).Identifier.ValueText;
                if (string.IsNullOrEmpty(name))
                    return null;

                if (parameterTypes[i].IsGenericParameter) // Generic is not supported
                    return null;

                parameters[i] = Expression.Parameter(parameterTypes[i], name);

                // Flow the incoming element's shape onto the lambda parameter
                if (parameterShapes != null && i < parameterShapes.Length)
                    SetTupleShape(parameters[i], parameterShapes[i]);
            }

            nestedParametersStartIndex = _nestedParameters.Count;
            nestedVariableStartIndex = _nextVariableIndex;
            _nestedParameters.AddRange(parameters);

            // Body
            var bodyNode = simple?.Block ?? (SyntaxNode?)parenthesized?.Block
                ?? simple?.ExpressionBody ?? parenthesized?.ExpressionBody;
            var body = Visit(bodyNode);
            if (body == null)
                return null;

            // A throw body takes the delegate's return type, e.g. Sum(x => throw ...)
            if (body is DelayThrowExpression delayThrow)
            {
                var returnType = (delegateType ?? throwReturnSource)?.GetMethod("Invoke")?.ReturnType;
                if (returnType == null || returnType.ContainsGenericParameters)
                    return null;

                body = returnType == typeof(void)
                    ? Expression.Throw(delayThrow.Exception)
                    : Expression.Throw(delayThrow.Exception, returnType);
            }

            // Build the exact delegate type when known, otherwise infer Func/Action
            LambdaExpression lambda;
            if (delegateType != null)
            {
                var returnType = delegateType.GetMethod("Invoke")!.ReturnType;
                if (returnType != typeof(void) && body.Type != returnType && TryConvertExpression(body, returnType) is { } convertedBody)
                    body = convertedBody;

                lambda = Expression.Lambda(delegateType, body, parameters);
            }
            else
                lambda = Expression.Lambda(body, parameters);

            // Save body's tuple shape as the delegate's return-type shape
            if (GetTupleShape(body) is { } bodyShape && lambda.Type.IsGenericType && lambda.Type.Name.StartsWith("Func`", StringComparison.Ordinal))
            {
                var slots = new ValueTupleShape?[lambda.Type.GetGenericArguments().Length];
                slots[slots.Length - 1] = bodyShape;
                SetTupleShape(lambda, new ValueTupleShape { Args = slots });
            }

            return lambda;
        }
        finally
        {
            if (parameters != null && nestedParametersStartIndex >= 0)
                for (var i = nestedParametersStartIndex + parameters.Length - 1; i >= nestedParametersStartIndex; i--)
                    _nestedParameters.RemoveAt(i);

            if (_variables != null)
                while (_variables.Count > 0 && _variables[_variables.Count - 1].Index >= nestedVariableStartIndex)
                    _variables.RemoveAt(_variables.Count - 1);
        }
    }
    private LambdaExpression? TryRebindLambdaToDelegate(LambdaExpression lambda, Type delegateType)
    {
        if (lambda.Type == delegateType || !typeof(Delegate).IsAssignableFrom(delegateType) || delegateType.GetMethod("Invoke") is not { } invoke)
            return null;

        var invokeParameters = invoke.GetParameters();
        if (invokeParameters.Length != lambda.Parameters.Count)
            return null;

        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var i = 0; i < invokeParameters.Length; i++)
            if (invokeParameters[i].ParameterType != lambda.Parameters[i].Type)
                return null;

        // The body implicitly converts to the delegate's return type
        var body = lambda.Body;
        if (invoke.ReturnType != body.Type)
        {
            if (invoke.ReturnType == typeof(void)
                || IsNullableType(body.Type) && !IsNullableType(invoke.ReturnType)
                || TryConvertExpression(body, invoke.ReturnType) is not { } converted)
                return null;

            body = converted;
        }

        return Expression.Lambda(delegateType, body, lambda.Parameters);
    }

}
