using System.Linq.Expressions;
using System.Reflection;

namespace TagBites.Expressions;

internal class ReflectionDetector : ExpressionVisitor
{
    public bool HasReflectionCall { get; private set; }


    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        HasReflectionCall |= node.Object != null
                             && (typeof(Type).IsAssignableFrom(node.Object.Type) || typeof(MemberInfo).IsAssignableFrom(node.Object.Type))
                             && node.Method.Name != nameof(Type.IsAssignableFrom);

        return base.VisitMethodCall(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        HasReflectionCall |= (typeof(Type).IsAssignableFrom(node.Member.DeclaringType) || typeof(MemberInfo).IsAssignableFrom(node.Member.DeclaringType))
                             && node.Member.Name != "Name"
                             && node.Member.Name != "IsValueType";

        return base.VisitMember(node);
    }
}
