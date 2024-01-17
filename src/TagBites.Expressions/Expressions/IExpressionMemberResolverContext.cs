using System.Linq.Expressions;

namespace TagBites.Expressions;

[PublicAPI]
public interface IExpressionMemberResolverContext
{
    Expression Instance { get; }
    object? InstanceTypeInfo { get; }

    string MemberName { get; }
    string? MemberFullPath { get; }


    ParameterExpression GetParameter(string name);
    Expression IncludeTypeInfo(Expression expression, object typeInfo);
}
