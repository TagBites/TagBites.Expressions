using System.Linq.Expressions;

namespace TagBites.Expressions.Extensions;

internal sealed class ReplaceExpressionVisitor(Expression source, Expression replacement) : ExpressionVisitor
{
    public override Expression? Visit(Expression? node)
    {
        if (node == null)
            return null;

        return ReferenceEquals(node, source)
            ? replacement
            : base.Visit(node)!;
    }
}
