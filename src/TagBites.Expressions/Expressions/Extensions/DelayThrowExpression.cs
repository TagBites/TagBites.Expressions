using System.Linq.Expressions;

namespace TagBites.Expressions.Extensions;

internal class DelayThrowExpression(Expression exception) : Expression
{
    public Expression Exception { get; } = exception;
}
