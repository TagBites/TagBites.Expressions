using System.Linq.Expressions;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TagBites.Expressions.Extensions;
using TagBites.Utils;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder : CSharpSyntaxVisitor<Expression>
{
    private static readonly MethodInfo s_typeInfoWrapper = typeof(ExpressionBuilder).GetMethod(nameof(TypeInfoWrapper), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo s_stringConcatObject = typeof(string).GetMethod(nameof(string.Concat), BindingFlags.Public | BindingFlags.Static, null, [typeof(object), typeof(object)], null)!;
    private static readonly MethodInfo s_stringCompare = typeof(string).GetMethod(nameof(string.Compare), BindingFlags.Public | BindingFlags.Static, null, [typeof(string), typeof(string)], null)!;
    private static readonly MethodInfo s_stringFormat = typeof(string).GetMethod(nameof(string.Format), [typeof(string), typeof(object[])])!;
    private static readonly MethodInfo s_objectToString = typeof(object).GetMethod("ToString", BindingFlags.Public | BindingFlags.Instance)!;
    private static readonly MethodInfo s_objectGetType = typeof(object).GetMethod(nameof(GetType))!;
    private static readonly MethodInfo s_typeIsAssignableFrom = typeof(Type).GetMethod(nameof(Type.IsAssignableFrom))!;
    private static readonly MethodInfo s_lvcGetValue = typeof(LambdaVariableContext).GetMethod(nameof(LambdaVariableContext.GetValue))!;
    private static readonly MethodInfo s_lvcSetValue = typeof(LambdaVariableContext).GetMethod(nameof(LambdaVariableContext.SetValue))!;
    private static readonly MethodInfo s_doubleIsNaN = typeof(double).GetMethod(nameof(double.IsNaN))!;
    private static readonly MethodInfo s_floatIsNaN = typeof(float).GetMethod(nameof(float.IsNaN))!;
    private static readonly PropertyInfo s_anonymousObjectIndexer = typeof(IDictionary<string, object>).GetProperty("Item")!;
    private static MethodInfo?[]? s_valueTupleCreate;

    private readonly ExpressionBuilderOptions _options;
    private readonly ExpressionBuilderContext _context;
    private readonly Type? _resultType;
    private readonly Type? _resultCastType;
    private readonly StringComparison _nameComparison;
    private Expression? _tmp;
    private Expression? _extensionInstance;
    private List<ParameterExpression>? _nestedParameters;
    private MemberResolverContext? _resolverContext;
    private ParameterExpression? _variableContextParameter;
    private List<(Type Type, string Name, int Index)>? _variables;
    private Dictionary<Expression, string>? _fullMemberPath;
    private int _nextVariableIndex;
    private bool? _checkedContext; // null == outside checked(x) and unchecked(x):
    private List<(Type SlotType, List<(string Name, Type Type, ValueTupleShape? TupleShape)> Shape)>? _anonymousObjects;
    private Dictionary<Expression, ValueTupleShape>? _tupleShapes;
    private Type? _targetType;

    public string? FirstError { get; private set; }
    public bool HasReflectionCall { get; private set; }

    public ExpressionBuilder(ExpressionBuilderOptions options, ExpressionBuilderContext context, Type? resultType, Type? resultCastType)
    {
        _options = options;
        _context = context;
        _nameComparison = _context.NameComparison;
        _targetType = resultType;
        _resultType = resultType;
        _resultCastType = resultCastType;
    }

    public LambdaExpression? CreateLambdaExpression(SyntaxNode node)
    {
        var expression = Visit(node);
        if (expression == null)
            return null;

        if (expression is DelayDefaultExpression)
        {
            if (_resultType == null)
            {
                ToError(node, "Cannot infer the type of 'default' because the result type is not set.");
                return null;
            }

            expression = Expression.Default(_resultType);
        }

        if (expression is DelayNewExpression or DelayThrowExpression)
        {
            ToError(node, "Cannot infer the type of the expression here.");
            return null;
        }

        if (_resultType != null && expression.Type != _resultType)
        {
            var isNullableResult = !_resultType.IsValueType || IsNullableType(_resultType);
            var converted = IsNullableType(expression.Type) && !isNullableResult
                ? null
                : TryConvertExpression(expression, _resultType);

            if (converted == null)
            {
                ToError(node, $"Result type is expected to be '{_resultType.GetFriendlyTypeName()}', but type '{expression.Type.GetFriendlyTypeName()}' is returned.");
                return null;
            }

            expression = converted;
        }

        if (_resultCastType != null && _resultCastType != expression.Type)
            expression = Expression.Convert(expression, _resultCastType);

        if (_variableContextParameter != null)
        {
            var innerLambda = Expression.Lambda(expression, _context.Parameters.Concat([_variableContextParameter]));
            var lvcConstructor = typeof(LambdaVariableContext).GetConstructor([typeof(int)])!;
            expression = Expression.Invoke(innerLambda, _context.Parameters.Cast<Expression>().Concat([Expression.New(lvcConstructor, Expression.Constant(_nextVariableIndex))]));
        }

        return Expression.Lambda(expression, _context.Parameters);
    }

    public override Expression? Visit(SyntaxNode? node)
    {
        if (node == null)
            return null;

        try
        {
            var expression = base.Visit(node);
            if (expression != null)
            {
                if (!_options.AllowReflection)
                    DetectReflection(expression);
            }

            return expression;
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

    private void DetectReflection(Expression expression)
    {
        if (HasReflectionCall)
            return;

        switch (expression)
        {
            case MethodCallExpression { Object: { } instance } m:
                HasReflectionCall = (typeof(Type).IsAssignableFrom(instance.Type) || typeof(MemberInfo).IsAssignableFrom(instance.Type))
                                    && m.Method.Name != nameof(Type.IsAssignableFrom);
                break;

            case MemberExpression me:
                HasReflectionCall = (typeof(Type).IsAssignableFrom(me.Member.DeclaringType) || typeof(MemberInfo).IsAssignableFrom(me.Member.DeclaringType))
                                    && me.Member.Name != "Name"
                                    && me.Member.Name != "IsValueType";
                break;
        }
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
}
