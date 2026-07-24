namespace TagBites.Expressions;

/// <summary>
/// The exception that is thrown when an expression cannot be parsed or bound.
/// </summary>
public sealed class ExpressionParserException(string message) : Exception(message);
