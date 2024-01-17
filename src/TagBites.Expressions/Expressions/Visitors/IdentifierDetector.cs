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
}
