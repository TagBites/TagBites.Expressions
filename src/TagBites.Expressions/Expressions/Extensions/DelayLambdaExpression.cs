using System.Linq.Expressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TagBites.Expressions.Extensions;

internal class DelayLambdaExpression(LambdaExpressionSyntax node) : Expression
{
    public LambdaExpressionSyntax Node { get; } = node;
}
