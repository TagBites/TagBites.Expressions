namespace TagBites.Expressions;

public sealed class ExpressionParserException : Exception
{
    public ExpressionParserException(string message)
        : base(message)
    { }
}
