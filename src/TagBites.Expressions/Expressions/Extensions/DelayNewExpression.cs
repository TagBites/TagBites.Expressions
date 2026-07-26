using System.Linq.Expressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TagBites.Expressions.Extensions;

internal class DelayNewExpression(ImplicitObjectCreationExpressionSyntax node) : Expression
{
    public ImplicitObjectCreationExpressionSyntax Node { get; } = node;
}
