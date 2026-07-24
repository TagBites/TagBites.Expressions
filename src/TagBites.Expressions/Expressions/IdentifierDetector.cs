using System.Linq.Expressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TagBites.Expressions;

internal class IdentifierDetector : ExpressionBuilder
{
    public IList<string> Identifiers { get; } = new List<string>();
    public IList<string> UnknownIdentifiers { get; } = new List<string>();

    public IdentifierDetector(ExpressionParserOptions options)
        : base(options)
    { }


    public override Expression VisitIdentifierName(IdentifierNameSyntax node)
    {
        var name = node.Identifier.Text;
        var result = base.VisitIdentifierName(node);

        if (result != null)
            Identifiers.Add(name);
        else
        {
            UnknownIdentifiers.Add(name);
            result = Expression.Constant(null, typeof(object));
        }

        return result;
    }
    public override Expression? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        // Resolve namespace-qualified types (e.g. System.Math.PI) before descending,
        // so their namespace segments are not visited as identifiers and wrongly reported as unknown.
        if (TryResolveNamespaceQualifiedType(node) is { } type)
            return Expression.Constant(type);

        return base.VisitMemberAccessExpression(node);
    }
}
