using System.Linq.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TagBites.Expressions;

[PublicAPI]
public static class ExpressionParser
{
    /// <summary>
    /// Parses, compiles and runs an expression, using <paramref name="arguments"/> as its parameters.
    /// </summary>
    /// <param name="expressionText">The expression to evaluate.</param>
    /// <param name="arguments">The parameters passed to the expression, matched by name.</param>
    /// <returns>The result of the expression, or <c>null</c>.</returns>
    /// <exception cref="ExpressionParserException">The expression cannot be parsed or bound.</exception>
    public static object? Invoke(string expressionText, params IList<ExpressionArgument> arguments) => Invoke<object>(expressionText, arguments);
    /// <summary>
    /// Parses, compiles and runs an expression, using <paramref name="arguments"/> as its parameters,
    /// and casts the result to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The expected result type.</typeparam>
    /// <param name="expressionText">The expression to evaluate.</param>
    /// <param name="arguments">The parameters passed to the expression, matched by name.</param>
    /// <returns>The result of the expression cast to <typeparamref name="T"/>, or <c>null</c>.</returns>
    /// <exception cref="ExpressionParserException">The expression cannot be parsed or bound.</exception>
    public static T? Invoke<T>(string expressionText, params IList<ExpressionArgument> arguments)
    {
        var options = new ExpressionParserOptions();
        object?[]? args = null;

        if (arguments.Count > 0)
        {
            var prms = new (Type, string)[arguments.Count];
            args = new object?[arguments.Count];

            for (var i = arguments.Count - 1; i >= 0; i--)
            {
                args[i] = arguments[i].Value;
                prms[i] = (arguments[i].Type, arguments[i].Name);
            }

            options.Parameters = prms;
        }

        var lambda = Parse(expressionText, options);
        var func = lambda.Compile();

        return (T)func.DynamicInvoke(args);
    }

    /// <summary>
    /// Parses, compiles and runs an expression, using <paramref name="options"/> for binding
    /// and <paramref name="arguments"/> as its parameter values.
    /// </summary>
    /// <param name="expressionText">The expression to evaluate.</param>
    /// <param name="options">The options that control parsing and binding.</param>
    /// <param name="arguments">The parameter values, in the order of <see cref="ExpressionParserOptions.Parameters"/>.</param>
    /// <returns>The result of the expression, or <c>null</c>.</returns>
    /// <exception cref="ExpressionParserException">The expression cannot be parsed or bound.</exception>
    public static object? Invoke(string expressionText, ExpressionParserOptions options, params object?[] arguments) => Invoke<object>(expressionText, options, arguments);
    /// <summary>
    /// Parses, compiles and runs an expression, using <paramref name="options"/> for binding
    /// and <paramref name="arguments"/> as its parameter values, and casts the result to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The expected result type.</typeparam>
    /// <param name="expressionText">The expression to evaluate.</param>
    /// <param name="options">The options that control parsing and binding.</param>
    /// <param name="arguments">The parameter values, in the order of <see cref="ExpressionParserOptions.Parameters"/>.</param>
    /// <returns>The result of the expression cast to <typeparamref name="T"/>, or <c>null</c>.</returns>
    /// <exception cref="ExpressionParserException">The expression cannot be parsed or bound.</exception>
    public static T? Invoke<T>(string expressionText, ExpressionParserOptions options, params object?[] arguments)
    {
        var lambda = Parse(expressionText, options);
        var func = lambda.Compile();

        return (T)func.DynamicInvoke(arguments);
    }

    /// <summary>
    /// Parses an expression and compiles it into a delegate of type <typeparamref name="TDelegate"/>.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type that matches the parameters and result of the expression.</typeparam>
    /// <param name="expressionText">The expression to compile.</param>
    /// <param name="options">The options that control parsing and binding, or <c>null</c> for the defaults.</param>
    /// <returns>The compiled delegate.</returns>
    /// <exception cref="ExpressionParserException">The expression cannot be parsed or bound.</exception>
    public static TDelegate Compile<TDelegate>(string expressionText, ExpressionParserOptions? options = null) where TDelegate : Delegate
    {
        var lambda = Parse(expressionText, options);
        return (TDelegate)lambda.Compile();
    }
    /// <summary>
    /// Tries to parse an expression and compile it into a delegate of type <typeparamref name="TDelegate"/>.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type that matches the parameters and result of the expression.</typeparam>
    /// <param name="expressionText">The expression to compile.</param>
    /// <param name="options">The options that control parsing and binding, or <c>null</c> for the defaults.</param>
    /// <param name="function">When this method returns, the compiled delegate, or <c>null</c> on failure.</param>
    /// <param name="errorMessage">When this method returns, the error message, or <c>null</c> on success.</param>
    /// <returns><c>true</c> if the expression was compiled; otherwise, <c>false</c>.</returns>
    public static bool TryCompile<TDelegate>(string expressionText, ExpressionParserOptions? options, out TDelegate? function, out string? errorMessage) where TDelegate : Delegate
    {
        if (!TryParse(expressionText, options, out var lambda, out errorMessage))
        {
            function = null;
            return false;
        }

        if (lambda!.Compile() is TDelegate t)
        {
            function = t;
            return true;
        }

        function = null;
        return false;
    }

    /// <summary>
    /// Parses an expression into a <see cref="LambdaExpression"/>.
    /// </summary>
    /// <param name="expressionText">The expression to parse.</param>
    /// <param name="options">The options that control parsing and binding, or <c>null</c> for the defaults.</param>
    /// <returns>The parsed lambda expression.</returns>
    /// <exception cref="ExpressionParserException">The expression cannot be parsed or bound.</exception>
    public static LambdaExpression Parse(string expressionText, ExpressionParserOptions? options = null)
    {
        return TryParse(expressionText, options, out var expression, out var errorMessage)
            ? expression!
            : throw new ExpressionParserException(errorMessage!);
    }
    /// <summary>
    /// Tries to parse an expression into a <see cref="LambdaExpression"/>.
    /// </summary>
    /// <param name="expressionText">The expression to parse.</param>
    /// <param name="options">The options that control parsing and binding, or <c>null</c> for the defaults.</param>
    /// <param name="expression">When this method returns, the parsed lambda expression, or <c>null</c> on failure.</param>
    /// <param name="errorMessage">When this method returns, the error message, or <c>null</c> on success.</param>
    /// <returns><c>true</c> if the expression was parsed; otherwise, <c>false</c>.</returns>
    public static bool TryParse(string expressionText, ExpressionParserOptions? options, out LambdaExpression? expression, out string? errorMessage)
    {
        options ??= new ExpressionParserOptions();

        var root = PrepareCore(expressionText);

        var error = root.ContainsDiagnostics
            ? root.GetDiagnostics().FirstOrDefault(x => x.Severity == DiagnosticSeverity.Error)
            : null;
        if (error != null)
        {
            expression = null;
            errorMessage = error.GetMessage();
        }
        else
        {
            var sv = new ExpressionBuilder(options.Common, options.GetPrepareContext(), options.ResultType, options.ResultCastType);

            try
            {
                expression = sv.CreateLambdaExpression(root);
                errorMessage = sv.FirstError;

                if (!options.AllowReflection && expression != null && sv.HasReflectionCall)
                {
                    expression = null;
                    errorMessage = "Reflection is not allowed.";
                }
            }
            catch (Exception e)
            {
                expression = null;
                errorMessage = e.Message;
            }
        }

        return expression != null;
    }

    /// <summary>
    /// Detects the identifiers used in an expression, without compiling it.
    /// </summary>
    /// <param name="expressionText">The expression to inspect.</param>
    /// <param name="options">The options that control parsing and binding, or <c>null</c> for the defaults.</param>
    /// <returns>
    /// A tuple with all identifiers found and the subset that does not resolve to a known
    /// parameter, variable, global member or type.
    /// </returns>
    public static (IList<string> Identifiers, IList<string> UnknownIdentifiers) DetectIdentifiers(string expressionText, ExpressionParserOptions? options = null)
    {
        options ??= new ExpressionParserOptions();

        var root = PrepareCore(expressionText);
        var visitor = new IdentifierDetector(options.Common, options.GetPrepareContext(), options.ResultType, options.ResultCastType);

        visitor.Visit(root);

        return (visitor.Identifiers, visitor.UnknownIdentifiers);
    }

    private static SyntaxNode PrepareCore(string expressionText)
    {
        if (string.IsNullOrWhiteSpace(expressionText))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(expressionText));

        return SyntaxFactory.ParseExpression(expressionText);
    }
}
