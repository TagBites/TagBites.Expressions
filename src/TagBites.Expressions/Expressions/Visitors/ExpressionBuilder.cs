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
    private readonly Expression? _thisParameter;
    private readonly List<ParameterExpression> _parameters;
    private Expression? _tmp;
    private Expression? _extensionInstance;
    private List<ParameterExpression>? _nestedParameters;
    private MemberResolverContext? _resolverContext;
    private ParameterExpression? _variableContextParameter;
    private List<(Type Type, string Name, int Index)>? _variables;
    private Dictionary<Expression, string>? _fullMemberPath;
    private int _nextVariableIndex;
    private bool _checkedContext;

    public string? FirstError { get; private set; }

    public ExpressionBuilder(ExpressionParserOptions options)
    {
        _options = options;
        _parameters = options.Parameters.Select(x => Expression.Parameter(x.Type, x.Name)).ToList();

        if (options.UseFirstParameterAsThis)
        {
            if (_parameters.Count > 0)
                _thisParameter = _parameters[0];
        }
        else if (options.GlobalMembers.TryGetValue("this", out var item) && item.Value != null)
            _thisParameter = Expression.Constant(item.Value, GetGlobalMemberType("this", item));
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

        // Enum arithmetic
        if (left.Type.IsEnum || right.Type.IsEnum)
            return TryBuildEnumBinaryOperation(node, expressionType, left, right);

        // C# promotes operands smaller than int (byte/sbyte/short/ushort/char) to int before applying the operator
        left = PromoteSmallInteger(left);
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

        // C# promotes operands smaller than int (byte/sbyte/short/ushort/char) to int
        if (expressionType is ExpressionType.OnesComplement or ExpressionType.Negate or ExpressionType.UnaryPlus)
            operand = PromoteSmallInteger(operand);

        if (_checkedContext && expressionType == ExpressionType.Negate)
            expressionType = ExpressionType.NegateChecked;

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

        return _checkedContext
            ? Expression.ConvertChecked(expression, type)
            : Expression.Convert(expression, type);
    }
    public override Expression? VisitDefaultExpression(DefaultExpressionSyntax node)
    {
        var type = ResolveType(node.Type);
        if (type == null)
            return null;

        return Expression.Default(type);
    }
    public override Expression? VisitTypeOfExpression(TypeOfExpressionSyntax node)
    {
        var type = ResolveType(node.Type);
        if (type == null)
            return null;

        return Expression.Constant(type, typeof(Type));
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
                    var name = methodNameSyntax.Identifier.Text;
                    var parameter = _parameters.FirstOrDefault(x => x.Name == name && x != _thisParameter);
                    if (parameter != null && typeof(Delegate).IsAssignableFrom(parameter.Type))
                    {
                        instanceExpression = parameter;
                        methodNameSyntax = null;
                        methodName = "Invoke";
                    }
                    // Member as method
                    else if (_options.GlobalMembers.TryGetValue(name, out var item)
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
                        methodName = methodNameSyntax.Identifier.Text;

                        if (_options.AllowRuntimeCast
                            && methodName is "typeis" or "typeas" or "typecast"
                            && ResolveCustomKeywords(node, methodName, parameters) is { } expression)
                        {
                            return expression;
                        }

                        instanceExpression = _thisParameter;
                    }
                }
                break;

            case MemberAccessExpressionSyntax ma:
                {
                    instanceExpression = Visit(ma.Expression);
                    if (instanceExpression == null)
                        return null;

                    methodNameSyntax = ma.Name;
                    methodName = methodNameSyntax.Identifier.Text;
                    break;
                }

            case MemberBindingExpressionSyntax mbs:
                {
                    instanceExpression = TryPop();
                    if (instanceExpression == null)
                        return null;

                    methodNameSyntax = mbs.Name;
                    methodName = methodNameSyntax.Identifier.Text;
                    break;
                }

            case GenericNameSyntax g:
                {
                    instanceExpression = _thisParameter;
                    methodNameSyntax = g;
                    methodName = methodNameSyntax.Identifier.Text;
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

        // Members
        if (_options.GlobalMembers.TryGetValue(name, out var item))
            return Expression.Constant(item.Value, GetGlobalMemberType(name, item));

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

                    for (var i = arrayType.RankSpecifiers.Count - 1; i >= 0; i--)
                    {
                        var rank = arrayType.RankSpecifiers[i].Rank;
                        elementType = rank == 1 ? elementType.MakeArrayType() : elementType.MakeArrayType(rank);
                    }

                    return elementType;
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
    private bool EnsureArgumentType(SyntaxNode node, Type parameterType, ref Expression argument)
    {
        if (TryConvertExpression(argument, parameterType) is not { } converted)
            return false;

        argument = converted;
        return true;
    }
    private static Expression? TryConvertExpression(Expression expression, Type targetType)
    {
        var sourceType = expression.Type;
        if (sourceType == targetType)
            return expression;

        if (targetType.IsAssignableFrom(sourceType))
            return ToCast(expression, targetType);

        if (TypeUtils.HasImplicitNumericConversion(sourceType, targetType))
            return ToCast(expression, targetType);

        var method = FindConversionOperator(sourceType, targetType, "op_Implicit");
        return method != null
            ? Expression.Convert(expression, targetType, method)
            : null;
    }
    private static MethodInfo? FindConversionOperator(Type sourceType, Type targetType, string operatorName)
    {
        return FindIn(sourceType) ?? FindIn(targetType);

        MethodInfo? FindIn(Type declaringType)
        {
            foreach (var method in declaringType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != operatorName)
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 1 || !parameters[0].ParameterType.IsAssignableFrom(sourceType))
                    continue;

                if (!targetType.IsAssignableFrom(method.ReturnType))
                    continue;

                return method;
            }

            return null;
        }
    }

    private Expression? TryBuildEnumBinaryOperation(SyntaxNode node, ExpressionType expressionType, Expression left, Expression right)
    {
        var leftIsEnum = left.Type.IsEnum;
        var rightIsEnum = right.Type.IsEnum;

        if (leftIsEnum && rightIsEnum)
        {
            if (left.Type != right.Type)
                return ToError(node, $"Operator cannot be applied to operands of enum types '{left.Type.GetFriendlyTypeName()}' and '{right.Type.GetFriendlyTypeName()}'.");

            // Linq.Expressions doesn't implement any operator for the enum type itself.
            var enumType = left.Type;
            var underlyingType = Enum.GetUnderlyingType(enumType);

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

        if (expressionType == ExpressionType.Subtract && leftIsEnum && TryConvertExpression(otherSide, underlyingType2) is { } subtractOperand)
            return Expression.Convert(Expression.Subtract(Expression.Convert(enumSide, underlyingType2), subtractOperand), enumType2);

        return ToError(node, $"Operator cannot be applied to operands of type '{left.Type.GetFriendlyTypeName()}' and '{right.Type.GetFriendlyTypeName()}'.");
    }

    private bool IsKnownIdentifier(string name)
    {
        return _variables?.Any(x => x.Name == name) == true
               || _nestedParameters?.Any(x => x.Name == name) == true
               || _parameters.Any(x => x.Name == name)
               || _options.GlobalMembers.ContainsKey(name);
    }
    private static string? TryGetNameOfValue(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax g => g.Identifier.Text,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            _ => null
        };
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
    private static Expression PromoteSmallInteger(Expression expression)
    {
        var underlying = Nullable.GetUnderlyingType(expression.Type) ?? expression.Type;
        if (underlying.IsEnum)
            return expression;

        switch (Type.GetTypeCode(underlying))
        {
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Char:
                return ToCast(expression, IsNullableType(expression.Type) ? typeof(int?) : typeof(int));

            default:
                return expression;
        }
    }
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

    private static IList<MethodInfo> GetIndexers(Type instanceType)
    {
        var members = new List<MethodInfo>();

        Collect(instanceType);

        if (instanceType.IsInterface)
            foreach (var type in instanceType.GetInterfaces())
                Collect(type);

        return members;

        void Collect(Type type)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var getter = property.GetMethod;
                if (property.GetIndexParameters().Length == 0 || getter is not { IsPublic: true })
                    continue;

                if (!members.Any(x => HasSameParameterTypes(x, getter)))
                    members.Add(getter);
            }
        }
    }
    private static bool HasSameParameterTypes(MethodInfo a, MethodInfo b)
    {
        var pa = a.GetParameters();
        var pb = b.GetParameters();
        if (pa.Length != pb.Length)
            return false;

        for (var i = 0; i < pa.Length; i++)
            if (pa[i].ParameterType != pb[i].ParameterType)
                return false;

        return true;
    }
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
    private IList<MethodInfo> GetExtensionMethods(Type instanceType, string name)
    {
        IList<MethodInfo>? members = null;

        // From known extensions
        if (TypeUtils.ContainsGenericDefinition(instanceType, typeof(IEnumerable<>)))
            FindMembers(typeof(Enumerable));

        // From included types
        if (_options.IncludedTypes.Count > 0)
            foreach (var type in _options.IncludedTypes)
                if (type.IsAbstract && type.IsSealed && type != typeof(Enumerable))
                    FindMembers(type);

        return members ?? Array.Empty<MethodInfo>();

        void FindMembers(Type type)
        {
            var nextMembers = type.GetMethods(BindingFlags.Public | BindingFlags.Static);

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

                    members ??= new List<MethodInfo>();
                    members.Add(item);
                }
            }
        }
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

            // A params method accepts more arguments than declared parameters.
            if (methodParameters.Length < methodArguments.Count && !info.HasParams)
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
                        TryExtractGenericArguments(methodParameters[Math.Min(i, methodParameters.Length - 1)].ParameterType, methodArguments[i].Type, argumentTypes);

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
                    var lambdaType = methodParameters[Math.Min(i, methodParameters.Length - 1)].ParameterType;
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
                    var elements = new List<Expression>(methodArguments.Count - paramsIndex);

                    for (var i = paramsIndex; i < methodArguments.Count; i++)
                    {
                        var element = methodArguments[i];
                        if (!EnsureArgumentType(relatedNode, elementType, ref element))
                            return null;

                        elements.Add(element);
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
        if (parameterType.IsAssignableFrom(argumentType))
            return true;

        // Numeric mismatch
        if (TypeUtils.IsNumericType(argumentType) && TypeUtils.IsNumericType(parameterType))
            return TypeUtils.HasImplicitNumericConversion(argumentType, parameterType);

        // User-defined implicit conversion
        if (FindConversionOperator(argumentType, parameterType, "op_Implicit") != null)
            return true;

        // Generic
        return TryExtractGenericArguments(parameterType, argumentType, null);
    }
    private static bool? IsUnsignedType(Type type)
    {
        var code = Type.GetTypeCode(type);
        return code is >= TypeCode.Byte and <= TypeCode.UInt64
            ? code is TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
            : null;
    }
    private static Type GetGlobalMemberType(string name, (Type? Type, object? Value) member)
    {
        if (member is { Type: not null, Value: not null } && !member.Type.IsAssignableFrom(member.Value.GetType()))
            throw new ArgumentException($"Member value is not type of member type. Member '{name}'.");

        return member.Type ?? member.Value?.GetType() ?? typeof(object);
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
            set => _memberFullPath = value;
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

            if (_type.IsValueType && Nullable.GetUnderlyingType(_type) == null && _type != typeof(void))
                _type = typeof(Nullable<>).MakeGenericType(_type);
        }


        public override Expression Reduce()
        {
            var member = Member;
            if (_type != member.Type)
                member = Convert(Member, _type);

            var constantExpression = _type == typeof(void)
                ? (Expression)Invoke(Constant(() => { }))
                : Constant(null, _type);

            return Condition(
                ToIsNotNull(Instance),
                member,
                constantExpression);
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
