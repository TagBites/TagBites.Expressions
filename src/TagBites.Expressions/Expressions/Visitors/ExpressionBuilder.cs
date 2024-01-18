using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TagBites.Utils;

namespace TagBites.Expressions;

// TODO namespaces support VisitQualifiedName

internal class ExpressionBuilder : CSharpSyntaxVisitor<Expression>
{
    private static MethodInfo? s_typeInfoWrapper;

    private readonly ExpressionParserOptions _options;
    private readonly ParameterExpression? _thisParameter;
    private readonly List<ParameterExpression> _parameters;
    private Expression? _tmp;
    private Expression? _extensionInstance;
    private List<ParameterExpression>? _nestedParameters;
    private MemberResolverContext? _resolverContext;
    private ParameterExpression? _variableContextParameter;
    private List<(Type Type, string Name, int Index)>? _variables;
    private int _nextVariableIndex;

    public string? FirstError { get; private set; }

    public ExpressionBuilder(ExpressionParserOptions options)
    {
        _options = options;
        _parameters = options.Parameters.Select(x => Expression.Parameter(x.Type, x.Name)).ToList();

        if (options.UseFirstParameterAsThis && _parameters.Count > 0)
            _thisParameter = _parameters[0];
    }


    public LambdaExpression? CreateLambdaExpression(SyntaxNode node)
    {
        var expression = Visit(node);
        if (expression == null)
            return null;

        var isNullableExpression = IsNullableType(expression.Type);
        if (_options.ResultType != null)
        {
            var isNullableResultType = IsNullableType(_options.ResultType);
            var isNullableResult = !_options.ResultType.IsValueType || isNullableResultType;
            var isAssignable = isNullableResultType && !isNullableExpression && Nullable.GetUnderlyingType(_options.ResultType) == expression.Type
                               || _options.ResultType.IsAssignableFrom(expression.Type);

            if (isNullableExpression && !isNullableResult || !isAssignable)
            {
                ToError(node, $"Result type is expected to be '{_options.ResultType.GetFriendlyTypeName()}', but type '{expression.Type.GetFriendlyTypeName()}' is returned.");
                return null;
            }

            //if (_options.ResultType != expression.Type && !isNullableResult)
            //    expression = Expression.Convert(expression, _options.ResultType);
        }

        if (_options.ResultCastType != null && _options.ResultCastType != expression.Type)
            expression = Expression.Convert(expression, _options.ResultCastType);

        if (_variableContextParameter != null)
        {
            var innerLambda = Expression.Lambda(expression, _parameters.Concat(new[] { _variableContextParameter }));
            expression = Expression.Invoke(innerLambda, _parameters.Cast<Expression>().Concat(new[] { Expression.New(_variableContextParameter.Type.GetConstructor(new[] { typeof(int) })!, Expression.Constant(_nextVariableIndex)) }));
        }

        return Expression.Lambda(expression, _parameters);
    }

    public override Expression? Visit(SyntaxNode? node)
    {
        if (node == null)
            return null;

        try
        {
            var expression = base.Visit(node);
            return expression != null && _options.UseReducedExpressions && expression is not DelayLambdaExpression && expression.NodeType == ExpressionType.Extension
                ? expression.Reduce()
                : expression;
        }
        catch (Exception e)
        {
            return ToError(node, e.Message);
        }
    }
    public override Expression? DefaultVisit(SyntaxNode node) => ToError(node);

    public override Expression? VisitCompilationUnit(CompilationUnitSyntax node)
    {
        if (node.Members.Count == 1 && node.Members[0] is GlobalStatementSyntax gs)
            return Visit(gs);

        if (node.Members.FirstOrDefault() is IncompleteMemberSyntax)
            return ToError(node, "Incomplete syntax.");

        return ToError(node);
    }
    public override Expression? VisitGlobalStatement(GlobalStatementSyntax node) => Visit(node.Statement);
    public override Expression? VisitExpressionStatement(ExpressionStatementSyntax node) => Visit(node.Expression);
    public override Expression? VisitParenthesizedExpression(ParenthesizedExpressionSyntax node) => Visit(node.Expression);
    public override Expression? VisitArgument(ArgumentSyntax node) => Visit(node.Expression);
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

        if (!EnsureTheSameTypes(node, ref whenTrue, ref whenFalse))
            return null;

        return Expression.Condition(condition, whenTrue, whenFalse);
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

            var expression = Visit(arm.Expression);
            if (expression == null)
                return null;

            // Condition
            Expression? condition = null;

            switch (arm.Pattern)
            {
                case DiscardPatternSyntax when i + 1 != node.Arms.Count || switchExpression != null:
                    return ToError(node, "Invalid switch syntax.");
                case DiscardPatternSyntax:
                    break;

                case ConstantPatternSyntax cps:
                    {
                        var value = Visit(cps.Expression);
                        if (value == null)
                            return null;

                        if (!EnsureArgumentType(arm.Pattern, governing.Type, ref value))
                            return ToError(arm.Pattern, "Switch governing and arm type mismatch.");

                        condition = Expression.MakeBinary(ExpressionType.Equal, governing, value);
                        break;
                    }

                default:
                    return ToError(arm.Pattern);
            }

            if (condition == null)
                switchExpression = expression;
            else
                paths.Add((condition, expression));
        }

        if (switchExpression == null)
            return ToError(node);

        // Convert to if-else
        for (var i = paths.Count - 1; i >= 0; i--)
        {
            if (switchExpression.Type != paths[i].Then.Type)
                return ToError(node.Arms[i].Expression, "Switch expressions types mismatch.");

            switchExpression = Expression.Condition(paths[i].When, paths[i].Then, switchExpression);
        }

        return switchExpression;
    }
    public override Expression? VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        var left = Visit(node.Left);
        if (left == null)
            return null;

        var right = Visit(node.Right);
        if (right == null)
            return null;

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
        if (expressionType == ExpressionType.Throw)
        {
            // operator ??
            if ((SyntaxKind)node.OperatorToken.RawKind == SyntaxKind.QuestionQuestionToken)
            {
                var condition = left;

                if (IsNullableType(left.Type))
                    left = Expression.MakeMemberAccess(left, left.Type.GetProperty(nameof(Nullable<int>.Value))!);

                if (!EnsureTheSameTypes(node, ref left, ref right))
                    return null;

                return Expression.Condition(ToIsNotNull(condition), left, right);
            }

            // Unknown
            return ToError(node, $"Unsupported binary operator '{node.OperatorToken.ValueText}'.");
        }

        // is operator
        if (expressionType == ExpressionType.TypeIs)
            return ToIsOperator(left, right);

        // as operator
        if (expressionType == ExpressionType.TypeAs)
            return ToAsOperator(node, left, right);

        // Operator + is not defined for string - use Contact instead
        if (expressionType == ExpressionType.Add && (left.Type == typeof(string) || right.Type == typeof(string)))
        {
            var contactMethod = typeof(string).GetMethod(nameof(string.Concat), BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(object), typeof(object) }, null);
            var toStringMethod = typeof(object).GetMethod("ToString", BindingFlags.Public | BindingFlags.Instance);

            if (left.Type != typeof(string))
                left = CallWhenNotNull(left, toStringMethod!);

            if (right.Type != typeof(string))
                right = CallWhenNotNull(right, toStringMethod!);

            return Expression.Call(null, contactMethod!, left, right);
        }

        // Operator < <= > >= is not defined for string - use Compare instead
        if (left.Type == typeof(string) && right.Type == typeof(string) && expressionType is ExpressionType.LessThan or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual)
        {
            var compareMethod = typeof(string).GetMethod(nameof(string.Compare), BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(string) }, null);
            var compareExpression = Expression.Call(null, compareMethod!, left, right);
            return Expression.MakeBinary(expressionType, compareExpression, Expression.Constant(0));
        }

        // Operator || && for bool only
        if (expressionType is ExpressionType.AndAlso or ExpressionType.OrElse)
        {
            var leftType = Nullable.GetUnderlyingType(left.Type) ?? left.Type;
            if (leftType != typeof(bool))
                return ToError(node.Left, "Expected boolean expression.");

            var rightType = Nullable.GetUnderlyingType(right.Type) ?? right.Type;
            if (rightType != typeof(bool))
                return ToError(node.Right, "Expected boolean expression.");
        }

        // Ensure types
        if (!EnsureTheSameTypes(node, ref left, ref right))
            return null;

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
            SyntaxKind.PlusToken => ExpressionType.UnaryPlus,
            SyntaxKind.MinusToken => ExpressionType.Negate,

            _ => ExpressionType.Throw
        };
        if (expressionType == ExpressionType.Throw)
            return ToError(node, $"Unsupported unary operator '{node.OperatorToken.ValueText}'.");

        return Expression.MakeUnary(expressionType, operand, null);
    }
    public override Expression? VisitCastExpression(CastExpressionSyntax node)
    {
        var expression = Visit(node.Expression);
        if (expression == null)
            return null;

        var type = ResolveType(node.Type);
        if (type == null)
            return null;

        return Expression.Convert(expression, type);
    }
    public override Expression? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var instance = Visit(node.Expression);
        if (instance == null)
            return null;

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

        return new ConditionalAccessExpression(instance, whenNotNull);
    }
    public override Expression? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // Parameters
        var parameters = ResolveParameters(node.ArgumentList.Arguments);
        if (parameters == null)
            return null;

        // Find instance, type, name syntax
        Expression? instanceExpression;
        Type? instanceType;
        SimpleNameSyntax methodNameSyntax;

        switch (node.Expression)
        {
            case IdentifierNameSyntax ins:
                {
                    instanceExpression = null;
                    methodNameSyntax = ins;
                }
                break;

            case MemberAccessExpressionSyntax ma:
                {
                    instanceExpression = Visit(ma.Expression);
                    if (instanceExpression == null)
                        return null;

                    methodNameSyntax = ma.Name;
                    break;
                }

            case MemberBindingExpressionSyntax mbs:
                {
                    instanceExpression = TryPop();
                    if (instanceExpression == null)
                        return null;

                    methodNameSyntax = mbs.Name;
                    break;
                }

            default:
                return ToError(node);
        }

        switch (instanceExpression)
        {
            case ConstantExpression { Value: Type type }:
                instanceExpression = null;
                instanceType = type;
                break;

            default:
                instanceType = instanceExpression?.Type;
                break;
        }

        // Method name with generics
        var methodName = methodNameSyntax.Identifier.Text;
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
            if (_options.AllowRuntimeCast
                && genericTypes == null
                && ResolveCustomKeywords(node, methodName, parameters) is { } expression)
            {
                return expression;
            }

            if (FirstError != null)
                return null;

            return ToError(node, $"Method '{methodName}' not found for this arguments.");
        }

        // Instance or static method
        {
            var methods = GetMethods(instanceType, methodName, instanceExpression == null ? BindingFlags.Static : BindingFlags.Instance);
            if (methods.Count > 0)
            {
                if (genericTypes != null)
                {
                    methods = methods
                        .Where(x => x.IsGenericMethodDefinition && x.GetGenericArguments().Length == genericTypes.Length)
                        .Select(x => x.MakeGenericMethod(genericTypes))
                        .ToList();
                }

                if (TryResolveMethodCall(node, instanceExpression, parameters, methods, out var expression))
                    return expression;
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
                    methods = methods
                        .Where(x => x.IsGenericMethodDefinition && x.GetGenericArguments().Length == genericTypes.Length)
                        .Select(x => x.MakeGenericMethod(genericTypes))
                        .ToList();
                }

                parameters = new[] { instanceExpression! }.Concat(parameters).ToList();

                var oldExtensionInstance = _extensionInstance;
                _extensionInstance = instanceExpression;
                try
                {
                    if (TryResolveMethodCall(node, null, parameters, methods, out var expression))
                        return expression;
                }
                finally
                {
                    _extensionInstance = oldExtensionInstance;
                }
            }
        }

        return ToError(node, $"Method '{methodName}' not found for this arguments.");
    }
    public override Expression? VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        // Parameters
        var parameters = ResolveParameters(node.ArgumentList.Arguments);
        if (parameters == null)
            return null;

        // Find instance, type, metod name
        var instanceExpression = Visit(node.Expression);
        if (instanceExpression == null)
            return null;

        return ResolveItemCall(node, instanceExpression, parameters);
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

        return ResolveItemCall(node, instanceExpression, parameters);
    }
    public override Expression? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        // Parameters
        var args = ResolveParameters(node.ArgumentList?.Arguments);
        if (args == null)
            return null;

        // Type
        var type = ResolveType(node.Type);
        if (type == null)
            return null;

        // Constructor
        var constructors = type.GetConstructors()
            .Select(x => (Method: x, Parameters: x.GetParameters()))
            .Where(x => HasMatchingParameters(x.Parameters, args))
            .OrderBy(x => x.Parameters.Length)
            .ToList();

        switch (constructors.Count)
        {
            case 0:
                return ToError(node, $"Constructor for '{type.GetFriendlyTypeName()}' not found.");
            case > 1 when constructors[0].Parameters.Length == constructors[1].Parameters.Length:
                return ToError(node, $"Ambiguous call for '{type.GetFriendlyTypeName()}' constructor.");
        }

        var constructorInfo = constructors[0].Method;
        var constructorParams = constructorInfo.GetParameters();

        // Default values
        for (var i = args.Count; i < constructorParams.Length; i++)
        {
            if (!constructorParams[i].HasDefaultValue)
                return ToError(node, "Mismatch argument count.");

            if (args is not List<Expression>)
                args = args.ToList();

            args.Add(Expression.Constant(constructorParams[i].DefaultValue));
        }

        // Create
        var instance = Expression.New(constructorInfo, args);

        // Initializer
        if (node.Initializer?.Expressions.Count > 0)
        {
            var bindings = new List<MemberBinding>();

            foreach (var item in node.Initializer.Expressions)
            {
                if (item is not AssignmentExpressionSyntax { Left: IdentifierNameSyntax identifier } ae)
                    return ToError(item);

                var memberName = identifier.Identifier.Text;
                var member = GetAssignMember(type, memberName);
                if (member == null)
                    return ToError(item, "Member not found.");

                var expression = Visit(ae.Right);
                if (expression == null)
                    return null;

                bindings.Add(Expression.Bind(member, expression));
            }

            return Expression.MemberInit(instance, bindings);
        }

        return instance;
    }
    public override Expression? VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
    {
        var type = ResolveType(node.Type.ElementType);
        if (type == null)
            return null;

        if (node.Type.RankSpecifiers.Count > 1 || node.Type.RankSpecifiers[0].Sizes[0] is not OmittedArraySizeExpressionSyntax)
            return ToError(node.Type, "Array with explicit range specifiers is not supported.");

        var expressions = new List<Expression>();

        if (node.Initializer != null)
            foreach (var expression in node.Initializer.Expressions)
            {
                var exp = Visit(expression);
                if (exp == null)
                    return null;
                expressions.Add(exp);
            }

        return Expression.NewArrayInit(type, expressions);
    }
    public override Expression? VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
    {
        var expressions = new List<Expression>();
        Type? type = null;

        foreach (var expression in node.Initializer.Expressions)
        {
            var exp = Visit(expression);
            if (exp == null)
                return null;

            expressions.Add(exp);

            if (type == null)
                type = exp.Type;
            else if (type != exp.Type)
                return ToError(node, "Type not found for implicit array creation.");
        }

        return Expression.NewArrayInit(type!, expressions);
    }

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
                        var hasValue = Expression.MakeMemberAccess(expression, expressionType.GetProperty(nameof(Nullable<int>.HasValue))!);
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

                    return ToIsOperator(expression, Expression.Constant(type));
                }

            // or, and
            case BinaryPatternSyntax p:
                {
                    var left = ResolvePattern(expression, p.Left);
                    if (left == null)
                        return null;
                    var right = ResolvePattern(expression, p.Right);
                    if (right == null)
                        return null;

                    return (SyntaxKind)p.OperatorToken.RawKind switch
                    {
                        SyntaxKind.OrKeyword => Expression.OrElse(left, right),
                        SyntaxKind.AndKeyword => Expression.AndAlso(left, right),
                        _ => ToError(pattern)
                    };
                }

            // >, <, >=, <=
            case RelationalPatternSyntax p:
                {
                    var right = Visit(p.Expression);
                    if (right == null)
                        return null;

                    if (!EnsureTheSameTypes(pattern, ref expression, ref right))
                        return null;

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

                    return Expression.MakeBinary(opr.Value, expression, right);
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

            // is ... x
            case DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax v } p:
                {
                    var type = ResolveType(p.Type);
                    if (type == null)
                        return null;

                    var name = v.Identifier.Text;
                    var declareExpression = DeclareVariable(v, Expression.Convert(expression, type), name);
                    if (declareExpression == null)
                        return null;

                    return Expression.AndAlso(
                        ToIsOperator(expression, Expression.Constant(type)),
                        Expression.Block(declareExpression, Expression.Constant(true)));
                }

            // is { } x
            case RecursivePatternSyntax { PositionalPatternClause: null } p:
                {
                    Expression checkExpression;

                    if (!IsNullableType(expressionType))
                        checkExpression = ToIsNotNull(expression);
                    else
                    {
                        checkExpression = Expression.MakeMemberAccess(expression, expressionType.GetProperty(nameof(Nullable<int>.HasValue))!);
                        expression = Expression.MakeMemberAccess(expression, expressionType.GetProperty(nameof(Nullable<int>.Value))!);
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

                    // Properties
                    if (p.PropertyPatternClause?.Subpatterns.Count > 0)
                    {
                        foreach (var property in p.PropertyPatternClause.Subpatterns)
                        {
                            var propertyName = property.NameColon!.Name.Identifier.Text;
                            var propertyValueExpression = ResolveCustomMember(expression, propertyName)
                                                          ?? ResolveMember(property, expression, propertyName);
                            if (propertyValueExpression == null)
                                return null;

                            var condition = ResolvePattern(propertyValueExpression, property.Pattern);
                            if (condition == null)
                                return null;

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

                    return checkExpression;
                }

            // is ... _
            case DiscardPatternSyntax:
                return expression;

            // Not supported yet
            case ListPatternSyntax:
            case SlicePatternSyntax:
                return ToError(pattern);
        }

        return ToError(pattern);
    }

    public override Expression VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => new DelayLambdaExpression(node);
    public override Expression VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => new DelayLambdaExpression(node);

    public override Expression? VisitNullableType(NullableTypeSyntax node)
    {
        var type = ResolveType(node.ElementType);
        if (type == null)
            return null;

        if (!type.IsValueType || Nullable.GetUnderlyingType(type) != null)
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
    public override Expression? VisitThisExpression(ThisExpressionSyntax node)
    {
        return _thisParameter ?? ToError(node, "Keyword 'this' is not valid in a static property or method.");
    }
    public override Expression? VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (Equals(node.Token.Value, "default"))
        {
            if (_options.ResultType != null)
                return Expression.Default(_options.ResultType);

            return ToError(node, "Default keyword is not supported.");
        }

        return Expression.Constant(node.Token.Value);
    }

    public override Expression? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
    {
        var format = new StringBuilder();
        var args = new List<Expression>();

        foreach (var content in node.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax item:
                    format.Append(item.TextToken.Text);
                    break;

                case InterpolationSyntax item:
                    var expression = Visit(item.Expression);
                    if (expression == null)
                        return null;

                    format.Append($"{{{args.Count}{item.FormatClause?.ToString()}}}");
                    args.Add(ToCast(expression, typeof(object)));
                    break;

                default:
                    return ToError(content);
            }
        }

        return Expression.Call(
            null,
            typeof(string).GetMethod(nameof(string.Format), new[] { typeof(string), typeof(object[]) })!,
            new Expression[]
            {
                Expression.Constant(format.ToString()),
                Expression.NewArrayInit(typeof(object), args)
            });
    }
    public override Expression? VisitTupleExpression(TupleExpressionSyntax node)
    {
        var args = ResolveParameters(node.Arguments);
        if (args == null)
            return null;

        var initMethod = typeof(ValueTuple)
            .GetMethods()
            .SingleOrDefault(x => x.Name == nameof(ValueTuple.Create) && x.GetParameters().Length == args.Count);
        if (initMethod == null)
            return ToError(node);

        return Expression.Call(null, initMethod.MakeGenericMethod(args.Select(x => x!.Type).ToArray()), args);
    }

    public override Expression? VisitIdentifierName(IdentifierNameSyntax node)
    {
        var name = node.Identifier.Text;

        // Variables
        if (_variables != null)
        {
            var (varType, _, varIndex) = _variables.FirstOrDefault(x => x.Name == name);
            if (varType != null)
                return Expression.Call(_variableContextParameter!, typeof(LambdaVariableContext).GetMethod(nameof(LambdaVariableContext.GetValue))!.MakeGenericMethod(varType), Expression.Constant(varIndex));
        }

        // Local parameter
        var parameter = _nestedParameters?.LastOrDefault(x => x.Name == name && x != _thisParameter);
        if (parameter != null)
            return parameter;

        // Parameter
        parameter = _parameters.FirstOrDefault(x => x.Name == name && x != _thisParameter);

        if (parameter != null)
            return parameter;

        // This
        if (_thisParameter != null)
        {
            var expression = ResolveCustomMember(_thisParameter, name)
                             ?? ResolveMember(node, _thisParameter, name, false);
            if (expression != null)
                return expression;
        }

        // Static type
        var type = ResolveType(node, name);
        if (type != null)
            return Expression.Constant(type);

        // Unknown
        return string.IsNullOrEmpty(name)
            ? ToError(node, "Missing identifier.")
            : ToError(node, $"Unknown identifier '{name}'.");
    }
    private Expression? DeclareVariable(SyntaxNode node, Expression expression, string name)
    {
        if (_variables?.Any(x => x.Name == name) == true
            || _parameters.Any(x => x.Name == name)
            || _nestedParameters?.Any(x => x.Name == name) == true)
        {
            return ToError(node, $"Variable '{name}' is already declared.");
        }

        var index = _nextVariableIndex++;

        _variableContextParameter ??= Expression.Parameter(typeof(LambdaVariableContext), "__lvc_");
        _variables ??= new List<(Type, string, int)>();
        _variables.Add((expression.Type, name, index));

        return Expression.Call(_variableContextParameter, typeof(LambdaVariableContext).GetMethod(nameof(LambdaVariableContext.SetValue))!, Expression.Constant(index), ToCast(expression, typeof(object)));
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

                    if (arrayType.RankSpecifiers.Count == 0)
                        return elementType.MakeArrayType();

                    return ToTypeError(type, null);
                }

            case GenericNameSyntax genericName:
                {
                    var elements = new List<Type>();
                    foreach (var item in genericName.TypeArgumentList.Arguments)
                    {
                        var elementType = ResolveType(item);
                        if (elementType == null)
                            return null;

                        elements.Add(elementType);
                    }

                    var genericType = ResolveType(type, genericName.Identifier.Text, elements.Count);
                    return genericType?.MakeGenericType(elements.ToArray());
                }

            case QualifiedNameSyntax { Right: IdentifierNameSyntax id } name:
                {
                    var t = ResolveType(name, id.Identifier.Text);
                    if (t != null && t.Namespace != name.Left.ToString())
                        return ToTypeError(type, null);

                    return t;
                }

            case IdentifierNameSyntax name:
                return ResolveType(name, name.Identifier.Text);

            default:
                return ToTypeError(type, null);
        }
    }
    private Type? ResolveType(SyntaxNode relatedNode, string typeName, int genericArguments = 0)
    {
        if (genericArguments > 0)
            typeName += "'" + genericArguments;

        if (_options.ResultType is { } resultType && resultType != typeof(object))
        {
            resultType = Nullable.GetUnderlyingType(resultType) ?? resultType;
            if (resultType.Name == typeName)
                return resultType;
        }

        if (_options.IncludedTypesMap.TryGetValue(typeName, out var type) && type != null)
            return type;

        foreach (var parameter in _parameters)
            if (parameter.Type.Name == typeName)
                return parameter.Type;

        return typeName switch
        {
            "TimeSpan" => typeof(TimeSpan),
            "DateTime" => typeof(DateTime),
            "DateTimeKind" => typeof(DateTimeKind),
            "DayOfWeek" => typeof(DayOfWeek),
            "StringComparison" => typeof(StringComparison),
            "StringSplitOptions" => typeof(StringSplitOptions),

            "List'1" => typeof(List<>),
            "IList'1" => typeof(IList<>),
            "IEnumerable'1" => typeof(IEnumerable<>),

            "CultureInfo" => typeof(CultureInfo),
            //"Type" => typeof(Type),

            "Math" => typeof(Math),
            "Enumerable" => typeof(Enumerable),

            _ => ToTypeError(relatedNode, typeName)
        };
    }
    private Expression? ResolveMember(SyntaxNode node, Expression expression, string name, bool setErrorWhenNotFound = true)
    {
        var staticType = (expression as ConstantExpression)?.Value as Type;
        var expressionType = staticType ?? expression.Type;

        // From instance
        for (var type = expressionType; type != null; type = type.BaseType)
        {
            var members = type.GetMember(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly | BindingFlags.Public);
            switch (members.Length)
            {
                case 1:
                    return Expression.MakeMemberAccess(staticType != null ? null : expression, members[0]);
                case > 1:
                    return setErrorWhenNotFound ? ToError(node, $"More then one member with name {name}.") : null;
            }

            if (type == typeof(object))
                break;
        }

        // From interface
        if (staticType == null)
        {
            foreach (var interfaceType in expressionType.GetInterfaces())
            {
                var members = interfaceType.GetMember(name, BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public);
                switch (members.Length)
                {
                    case 1:
                        return Expression.MakeMemberAccess(expression, members[0]);
                    case > 1:
                        return setErrorWhenNotFound ? ToError(node, $"More then one member with name {name}.") : null;
                }
            }
        }

        return setErrorWhenNotFound ? ToError(node, $"Unknown member {name}.") : null;
    }
    private Expression? ResolveCustomMember(Expression expression, string name)
    {
        var resolver = _options.CustomPropertyResolver;
        if (resolver == null)
            return null;

        _resolverContext ??= new MemberResolverContext(this);
        _resolverContext.Switch(_extensionInstance, expression, name);

        return resolver(_resolverContext);
    }

    private Expression? TryResolveLambda(LambdaExpressionSyntax node, Type[] parameterTypes, Type? resultType)
    {
        var simple = node as SimpleLambdaExpressionSyntax;
        var parenthesized = node as ParenthesizedLambdaExpressionSyntax;
        var parametersSyntax = simple != null
            ? new[] { simple.Parameter }
            : parenthesized!.ParameterList.Parameters.ToArray();
        if (parametersSyntax.Length != parameterTypes.Length)
            return null;

        var nestedParametersStartIndex = -1;
        var nestedVariableStartIndex = -1;
        ParameterExpression[]? parameters = null;

        _nestedParameters ??= new List<ParameterExpression>();
        try
        {
            // Parameters
            parameters = new ParameterExpression[parameterTypes.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var name = parametersSyntax[i].Identifier.Text;
                if (string.IsNullOrEmpty(name))
                    return null;

                if (parameterTypes[i].IsGenericParameter) // Generic is not supported
                    return null;

                parameters[i] = Expression.Parameter(parameterTypes[i], name);
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

            // Result
            return Expression.Lambda(body, parameters);
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
    private IList<Expression>? ResolveParameters(IEnumerable<ArgumentSyntax>? node)
    {
        if (node == null)
            return Array.Empty<Expression>();

        var parameters = new List<Expression>();

        foreach (var arg in node)
        {
            var argExpression = Visit(arg);
            if (argExpression == null)
                return null;

            parameters.Add(argExpression);
        }

        return parameters;
    }
    private Expression? ResolveItemCall(SyntaxNode relatedNode, Expression instanceExpression, IList<Expression> parameters)
    {
        var instanceType = instanceExpression.Type;
        if (instanceType.IsArray)
            return Expression.ArrayAccess(instanceExpression, parameters);

        // Select method override
        var methods = GetIndexers(instanceType);

        if (methods.Count > 0 && TryResolveMethodCall(relatedNode, instanceExpression, parameters, methods, out var expression))
            return expression;

        return ToError(relatedNode, "Indexer not found for this arguments.");
    }
    private Expression? ResolveCustomKeywords(SyntaxNode node, string methodName, IList<Expression> parameters)
    {
        // ReSharper disable StringLiteralTypo
        if (methodName is not ("typeis" or "typeas" or "typecast")
            || parameters.Count != 2
            || parameters[0] is not ConstantExpression { Value: string typeName })
        {
            return null;
        }

        var runtimeType = Type.GetType(typeName);
        if (runtimeType == null)
            return ToError(node, $"Runtime type '{typeName}' not found.");

        var expression = parameters[1];

        return methodName switch
        {
            "typeis" => ToIsOperator(expression, Expression.Constant(runtimeType)),
            "typeas" => ToAsOperator(node, expression, Expression.Constant(runtimeType)),
            "typecast" => ToCastOperator(node, expression, Expression.Constant(runtimeType), false),
            _ => throw new ArgumentOutOfRangeException()
        };
        // ReSharper restore StringLiteralTypo
    }

    private bool EnsureTheSameTypes(SyntaxNode node, ref Expression e1, ref Expression e2)
    {
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

        if (Nullable.GetUnderlyingType(t1) != null)
        {
            if (Nullable.GetUnderlyingType(t2) == null && t2.IsValueType)
                e2 = Expression.Convert(e2, typeof(Nullable<>).MakeGenericType(t2));
        }
        else if (Nullable.GetUnderlyingType(t2) != null && t1.IsValueType)
            e1 = Expression.Convert(e1, typeof(Nullable<>).MakeGenericType(t1));

        // Numeric mismatch
        if (TypeUtils.IsNumericType(t1) && TypeUtils.IsNumericType(t2))
        {
            var c1 = Type.GetTypeCode(t1);
            var c2 = Type.GetTypeCode(t2);

            if (c1 is TypeCode.Decimal or TypeCode.Double && c2 is TypeCode.Decimal or TypeCode.Double)
            {
                ToError(node, "Can not apply operator to decimal and double types.");
                return false;
            }

            if (c1 > c2)
                e2 = ToCast(e2, t1);
            else
                e1 = ToCast(e1, t2);
        }

        // Cast
        if (t1.IsAssignableFrom(t2))
            e2 = ToCast(e2, t1);
        else if (t2.IsAssignableFrom(t1))
            e1 = ToCast(e1, t2);

        return true;
    }
    private bool EnsureArgumentType(SyntaxNode node, Type parameterType, ref Expression argument)
    {
        var argumentType = argument.Type;
        if (parameterType == argumentType)
            return true;

        // Numeric mismatch
        if (TypeUtils.IsNumericType(argumentType) && TypeUtils.IsNumericType(parameterType))
        {
            var argumentCode = Type.GetTypeCode(argumentType);
            var parameterCode = Type.GetTypeCode(parameterType);

            if (argumentCode is TypeCode.Decimal or TypeCode.Double && parameterCode is TypeCode.Decimal or TypeCode.Double)
                return false;

            if (argumentCode < parameterCode)
            {
                argument = ToCast(argument, parameterType);
                return true;
            }

            return false;
        }

        // Missing type cast
        if (parameterType.IsAssignableFrom(argumentType))
        {
            argument = ToCast(argument, parameterType);
            return true;
        }

        return false;
    }

    private void Push(Expression expression) => _tmp = expression;
    private Expression? Pop(SyntaxNode node)
    {
        var r = _tmp;
        _tmp = null;
        return r ?? ToError(node, "Invalid syntax.");
    }
    private Expression? TryPop()
    {
        var r = _tmp;
        _tmp = null;
        return r;
    }

    private Expression? ToError(SyntaxNode node, string? message = null)
    {
        var code = node.ToFullString();
        if (string.IsNullOrEmpty(code))
        {
            node = node.Parent!;
            code = node.ToFullString();
        }

        var location = node.GetLocation().GetLineSpan().StartLinePosition;

        FirstError = $"{message ?? $"Unsupported expression of type {node.GetType().Name}."}{Environment.NewLine}at ({location}): {code}";
        return null;
    }
    private Type? ToTypeError(SyntaxNode node, string? typeName)
    {
        FirstError = $"Unknown type '{typeName ?? node.ToString()}'.";
        return null;
    }

    private static Expression ToIsNotNull(Expression expression)
    {
        if (Nullable.GetUnderlyingType(expression.Type) is { })
            return Expression.MakeMemberAccess(expression, expression.Type.GetProperty("HasValue")!);

        if (expression.Type.IsValueType)
            return Expression.Constant(true);

        return Expression.NotEqual(expression, Expression.Constant(null, expression.Type));
    }
    private static Expression ToCast(Expression expression, Type type) => expression.Type != type ? Expression.Convert(expression, type) : expression;
    private static Expression CallWhenNotNull(Expression instance, MethodInfo method)
    {
        if (Nullable.GetUnderlyingType(instance.Type) is { })
            return new ConditionalAccessExpression(instance, Expression.Call(Expression.MakeMemberAccess(instance, instance.Type.GetProperty("Value")!), method));

        return instance.Type.IsValueType
            ? Expression.Call(instance, method)
            : new ConditionalAccessExpression(instance, Expression.Call(instance, method));
    }
    private static Expression ToIsOperator(Expression left, Expression right)
    {
        var expressionType = left.Type;

        if (expressionType.IsValueType && !IsNullableType(expressionType))
        {
            var castType = (Type)((ConstantExpression)right).Value;
            castType = Nullable.GetUnderlyingType(castType) ?? castType;
            expressionType = Nullable.GetUnderlyingType(expressionType) ?? expressionType;

            return Expression.MakeBinary(ExpressionType.Equal, Expression.Constant(expressionType), Expression.Constant(castType));
        }

        var leftType = Expression.Call(left, typeof(object).GetMethod("GetType")!);

        return Expression.AndAlso(
            ToIsNotNull(left),
            Expression.Call(right, typeof(Type).GetMethod("IsAssignableFrom")!, leftType));
    }
    private Expression? ToAsOperator(SyntaxNode node, Expression left, Expression right)
    {
        var castOperation = ToCastOperator(node, left, right, true);
        if (castOperation == null)
            return null;

        var condition = ToIsOperator(left, right);

        return Expression.Condition(condition, castOperation, Expression.Constant(null, castOperation.Type));
    }
    private Expression? ToCastOperator(SyntaxNode node, Expression left, Expression right, bool usedByAsOperator)
    {
        var castType = (Type)((ConstantExpression)right).Value;
        var expressionType = left.Type;

        if (usedByAsOperator && castType.IsValueType && !IsNullableType(castType))
            return ToError(node, "The as operator must be used with a reference type or nullable type");

        if (castType.IsAssignableFrom(expressionType) || expressionType.IsAssignableFrom(castType))
            return Expression.Convert(left, castType);

        return ToError(node, $"Cannot convert value type '{left.Type}' to '{right.Type}' using build-in conversion.");
    }

    private static IList<MethodInfo> GetIndexers(Type instanceType) => GetMethods(instanceType, "get_Item", BindingFlags.Instance);
    private static IList<MethodInfo> GetMethods(Type instanceType, string name, BindingFlags additionalFlags)
    {
        var members = new List<MethodInfo>();
        var names = new HashSet<string>();

        for (var type = instanceType; type != null; type = type.BaseType)
        {
            var nextMembers = type.GetMethods(BindingFlags.Public | additionalFlags);

            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var i = 0; i < nextMembers.Length; i++)
            {
                var item = nextMembers[i];
                if (item.Name == name && names.Add(item.ToString()))
                    members.Add(item);
            }

            if (type == typeof(object))
                break;
        }

        foreach (var type in instanceType.GetInterfaces())
        {
            var nextMembers = type.GetMethods(BindingFlags.Public | additionalFlags);

            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var i = 0; i < nextMembers.Length; i++)
            {
                var item = nextMembers[i];
                if (item.Name == name && names.Add(item.ToString()))
                    members.Add(item);
            }
        }

        return members;
    }
    private static IList<MethodInfo> GetExtensionMethods(Type instanceType, string name)
    {
        var members = new List<MethodInfo>();

        // Extensions
        if (TypeUtils.ContainsGenericDefinition(instanceType, typeof(IEnumerable<>)))
        {
            var nextMembers = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static);

            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var i = 0; i < nextMembers.Length; i++)
            {
                var item = nextMembers[i];
                if (item.Name == name && item.GetCustomAttribute<ExtensionAttribute>() != null)
                {
                    var thisParameter = item.GetParameters().FirstOrDefault();
                    if (thisParameter == null)
                        continue;

                    if (!IsMatchingParameterType(thisParameter.ParameterType, instanceType))
                        continue;

                    members.Add(item);
                }
            }
        }

        return members;
    }
    private static MemberInfo? GetAssignMember(Type type, string name)
    {
        for (; type != null!; type = type.BaseType!)
        {
            var member = (MemberInfo?)type.GetProperty(name) ?? type.GetField(name);
            if (member != null)
                return member;
        }

        return null;
    }
    private static bool TryExtractGenericArguments(Type parameterWithGeneric, Type argumentType, IList<(string, Type)>? argumentTypes)
    {
        if (parameterWithGeneric.IsGenericParameter)
        {
            argumentTypes?.Add((parameterWithGeneric.Name, argumentType));
            return true;
        }

        if (!parameterWithGeneric.IsGenericType)
            return parameterWithGeneric == argumentType;

        if (parameterWithGeneric.IsInterface)
        {
            var definition = parameterWithGeneric.IsGenericTypeDefinition
                ? parameterWithGeneric
                : parameterWithGeneric.GetGenericTypeDefinition();

            if (argumentType != definition && (argumentType.IsGenericType && argumentType.GetGenericTypeDefinition() != definition || argumentType.IsArray))
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

    private bool TryResolveMethodCall(SyntaxNode relatedNode, Expression? instanceExpression, IList<Expression> arguments, IEnumerable<MethodInfo> candidates, out Expression? expression)
    {
        expression = null;

        // Select best method
        MethodCallInfo? bestMethod = null;
        List<MethodCallInfo>? ambiguous = null;

        foreach (var item in candidates)
        {
            var info = ToMatchingMethod(item);
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
                ambiguous ??= new List<MethodCallInfo>();
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

        expression = Expression.Call(instanceExpression, bestMethod.Method, bestMethod.Arguments);
        return true;

        MethodCallInfo? ToMatchingMethod(MethodInfo x)
        {
            var info = new MethodCallInfo(x, arguments);
            ref var method = ref info.Method;
            var methodParameters = info.Parameters;
            var methodArguments = info.Arguments;

            if (methodParameters.Length < methodArguments.Count)
                return null;

            // Try extract arguments
            Type[]? genericParameters = null;
            Type?[]? genericArguments = null;

            if (method.IsGenericMethodDefinition)
            {
                genericParameters = method.GetGenericArguments();
                genericArguments = new Type[genericParameters.Length];

                var argumentTypes = new List<(string, Type)>();

                for (var i = 0; i < methodArguments.Count; i++)
                    if (methodArguments[i] is not DelayLambdaExpression)
                        TryExtractGenericArguments(methodParameters[i].ParameterType, methodArguments[i].Type, argumentTypes);

                for (var i = 0; i < genericParameters.Length; i++)
                {
                    var type = genericParameters[i];
                    var fullType = argumentTypes.FirstOrDefault(y => y.Item1 == type.Name).Item2;
                    genericArguments[i] = fullType;
                }
            }

            if (method.IsGenericMethodDefinition && genericArguments!.All(y => y != null))
            {
                method = method.MakeGenericMethod(genericArguments!);
                methodParameters = method.GetParameters();
            }

            // Parse lambda
            for (var i = 0; i < methodArguments.Count; i++)
                if (methodArguments[i] is DelayLambdaExpression dl)
                {
                    // Resolve parameters
                    var lambdaType = methodParameters[i].ParameterType;
                    if (!lambdaType.IsGenericType)
                        return null;

                    Type? resultType = null;
                    var lambdaParameters = lambdaType.GetGenericArguments();

                    for (var j = 0; j < lambdaParameters.Length; j++)
                    {
                        var item = lambdaParameters[j];
                        if (item.IsGenericParameter)
                        {
                            for (var k = 0; k < genericParameters!.Length; k++)
                                if (genericParameters[k].Name == item.Name)
                                {
                                    if (genericArguments![k] != null)
                                        lambdaParameters[j] = genericArguments[k]!;
                                    break;
                                }
                        }
                    }

                    // var invokeMethod = lambdaType.GetMethod("Invoke")!;
                    if (lambdaType.Name.StartsWith("Func`"))
                    {
                        resultType = lambdaParameters.Last();
                        lambdaParameters = lambdaParameters.Take(lambdaParameters.Length - 1).ToArray();
                    }

                    // Expression
                    var expression = TryResolveLambda(dl.Node, lambdaParameters, resultType);
                    if (expression == null)
                        return null;

                    methodArguments[i] = expression;

                    // Extract generics
                    if (method.IsGenericMethodDefinition)
                    {
                        var argumentTypes = new List<(string, Type)>();
                        TryExtractGenericArguments(methodParameters[i].ParameterType, methodArguments[i].Type, argumentTypes);

                        for (var j = 0; j < genericArguments!.Length; j++)
                        {
                            if (genericArguments[j] != null)
                                continue;

                            var type = genericParameters![i];
                            var fullType = argumentTypes.FirstOrDefault(y => y.Item1 == type.Name).Item2;
                            genericArguments[j] = fullType;
                        }
                    }
                }


            // Make
            if (method.IsGenericMethodDefinition)
            {
                if (genericArguments!.Any(y => y == null))
                    return null;

                method = method.MakeGenericMethod(genericArguments!);
                methodParameters = method.GetParameters();
            }

            // Cast method arguments
            for (var i = 0; i < methodArguments.Count; i++)
            {
                var parameterType = methodParameters[i].ParameterType;
                var argumentType = methodArguments[i].Type;

                if (parameterType != argumentType)
                {
                    var argument = methodArguments[i];
                    if (!EnsureArgumentType(relatedNode, parameterType, ref argument))
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

            return info;
        }
    }
    private static MethodCallInfo? GetBestMatchingMethod(MethodCallInfo method1, MethodCallInfo method2)
    {
        var args = method1.RawArguments;

        for (var i = 0; i < args.Count; i++)
        {
            var argType = args[i].Type;
            var t1 = method1.Parameters[method1.HasParams ? Math.Min(i, method1.Parameters.Length - 1) : i].ParameterType;
            var t2 = method2.Parameters[method2.HasParams ? Math.Min(i, method2.Parameters.Length - 1) : i].ParameterType;

            if (GetBestMatchingType(argType, t1, t2) is { } best)
                return best == t1 ? method1 : method2;
        }

        if (method1.Method.IsGenericMethod.CompareTo(method2.Method.IsGenericMethod) is var genericDiff && genericDiff != 0)
            return genericDiff < 0 ? method1 : method2;

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

        // Assignable
        var a1 = type1.IsAssignableFrom(argType);
        var a2 = type2.IsAssignableFrom(argType);

        if (a1 && !a2)
            return type1;
        if (a2 && !a1)
            return type2;

        // Matching
        var m1 = IsMatchingParameterType(type2, type1);
        var m2 = IsMatchingParameterType(type1, type2);

        if (m1 && !m2)
            return type1;
        if (m2 && !m1)
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

    private static bool IsNullableType(Type type) => Nullable.GetUnderlyingType(type) != null;
    private static bool HasMatchingParameters(IList<ParameterInfo> parameters, IList<Expression> arguments)
    {
        if (parameters.Count < arguments.Count)
            return false;

        for (var i = 0; i < arguments.Count; i++)
        {
            var ept = arguments[i].Type;
            var mpt = parameters[i].ParameterType;

            if (!IsMatchingParameterType(mpt, ept))
                return false;
        }

        return true;
    }
    private static bool IsMatchingParameterType(Type parameterType, Type argumentType)
    {
        if (!parameterType.IsAssignableFrom(argumentType))
        {
            // Numeric mismatch
            if (TypeUtils.IsNumericType(argumentType) && TypeUtils.IsNumericType(parameterType))
            {
                var argumentCode = Type.GetTypeCode(argumentType);
                var parameterCode = Type.GetTypeCode(parameterType);

                if (argumentCode is TypeCode.Decimal or TypeCode.Double && parameterCode is TypeCode.Decimal or TypeCode.Double)
                    return false;

                return argumentCode < parameterCode;
            }

            // Generic
            return TryExtractGenericArguments(parameterType, argumentType, null);
        }

        return true;
    }
    private static bool? IsUnsignedType(Type type)
    {
        var code = Type.GetTypeCode(type);
        return code is >= TypeCode.Byte and <= TypeCode.UInt64
            ? code is TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
            : null;
    }

    private static Expression WrapWithTypeInfo(Expression expression, object typeInfo)
    {
        s_typeInfoWrapper ??= typeof(ExpressionBuilder).GetMethod(nameof(TypeInfoWrapper), BindingFlags.Static | BindingFlags.NonPublic)!;
        var method = s_typeInfoWrapper.MakeGenericMethod(expression.Type);
        return Expression.Call(method, expression, Expression.Constant(typeInfo));
    }
    private static object? ExtractTypeInfo(Expression? expression)
    {
        if (expression is MethodCallExpression { Method.IsGenericMethod: true } mc && mc.Method.GetGenericMethodDefinition() == s_typeInfoWrapper)
            return ((ConstantExpression)mc.Arguments[1]).Value;

        return null;
    }
    private static T TypeInfoWrapper<T>(T value, object typeInfo) => value;

    private class MethodCallInfo
    {
        public MethodInfo Method;

        public ParameterInfo[] Parameters { get; }
        public bool HasParams { get; }

        public IList<Expression> RawArguments { get; }
        public List<Expression> Arguments { get; }

        public MethodCallInfo(MethodInfo method, IList<Expression> rawArguments)
        {
            Method = method;
            Parameters = method.GetParameters();
            HasParams = Parameters.Length > 0 && Parameters[Parameters.Length - 1].IsDefined(typeof(ParamArrayAttribute), false);

            RawArguments = rawArguments;
            Arguments = rawArguments.ToList();
        }


        public override string ToString() => Method.ToString();
    }
    private class MemberResolverContext : IExpressionMemberResolverContext
    {
        private readonly ExpressionBuilder _visitor;
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
        }

        public MemberResolverContext(ExpressionBuilder visitor) => _visitor = visitor;


        public void Switch(Expression? expressionInstance, Expression instance, string memberName)
        {
            Instance = instance;
            InstanceTypeInfo = ExtractTypeInfo(instance) ?? (instance is ParameterExpression ? ExtractTypeInfo(expressionInstance) : null);

            MemberName = memberName;
            _memberFullPath = null;
        }

        public ParameterExpression GetParameter(string name) => _visitor._parameters.First(x => x.Name == name);
        public Expression IncludeTypeInfo(Expression expression, object typeInfo) => WrapWithTypeInfo(expression, typeInfo);
    }
    private class DelayLambdaExpression : Expression
    {
        public LambdaExpressionSyntax Node { get; }

        public DelayLambdaExpression(LambdaExpressionSyntax node)
        {
            Node = node;
        }
    }
    private class ConditionalAccessExpression : Expression
    {
        private readonly Type _type;

        private Expression Instance { get; }
        private Expression Member { get; }

        public override Type Type => _type;
        public override bool CanReduce => true;
        public override ExpressionType NodeType => ExpressionType.Extension;

        public ConditionalAccessExpression(Expression instance, Expression member)
        {
            Instance = instance;
            Member = member;

            _type = member.Type;

            if (_type.IsValueType && Nullable.GetUnderlyingType(_type) == null)
                _type = typeof(Nullable<>).MakeGenericType(_type);
        }


        public override Expression Reduce()
        {
            var member = Member;
            if (_type != member.Type)
                member = Convert(Member, _type);

            return Condition(
                ToIsNotNull(Instance),
                member,
                Constant(null, _type));
        }
        public override string ToString()
        {
            var instance = Instance.ToString();
            var member = Member.ToString();

            if (member.StartsWith(instance + "."))
                return $"{instance}?{member.Substring(instance.Length)}";

            return $"({instance != null} ? {member} : default)";
        }
    }

    private class LambdaVariableContext
    {
        private readonly object[] _values;

        public LambdaVariableContext(int count) => _values = new object[count];


        public T GetValue<T>(int index) => (T)_values[index];
        public void SetValue(int index, object value) => _values[index] = value;
    }
}
