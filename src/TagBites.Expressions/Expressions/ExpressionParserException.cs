namespace TagBites.Expressions;

/// <summary>
/// The exception that is thrown when an expression cannot be parsed or bound.
/// </summary>
/// <param name="message">The message that describes the error.</param>
public sealed class ExpressionParserException(string message) : Exception(message);
