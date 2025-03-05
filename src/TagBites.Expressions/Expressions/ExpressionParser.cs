using System.Linq.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TagBites.Expressions;

[PublicAPI]
public static class ExpressionParser
{
    public static object? Invoke(string expressionText, params IList<ExpressionArgument> arguments) => Invoke<object>(expressionText, arguments);
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

    public static object? Invoke(string expressionText, ExpressionParserOptions options, params object?[] arguments) => Invoke<object>(expressionText, options, arguments);
    public static T? Invoke<T>(string expressionText, ExpressionParserOptions options, params object?[] arguments)
    {
        var lambda = Parse(expressionText, options);
        var func = lambda.Compile();

        return (T)func.DynamicInvoke(arguments);
    }

    public static TDelegate Compile<TDelegate>(string expressionText, ExpressionParserOptions? options = null) where TDelegate : Delegate
    {
        var lambda = Parse(expressionText, options);
        return (TDelegate)lambda.Compile();
    }
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

    public static LambdaExpression Parse(string expressionText, ExpressionParserOptions? options = null)
    {
        return TryParse(expressionText, options, out var expression, out var errorMessage)
            ? expression!
            : throw new ExpressionParserException(errorMessage!);
    }
    public static bool TryParse(string expressionText, ExpressionParserOptions? options, out LambdaExpression? expression, out string? errorMessage)
    {
        options ??= new ExpressionParserOptions();

        var root = PrepareCore(expressionText);
        var diagnostics = root.GetDiagnostics();

        var error = diagnostics.FirstOrDefault(x => x.Severity == DiagnosticSeverity.Error && x.Id != "CS1002");
        if (error != null)
        {
            expression = null;
            errorMessage = error.GetMessage();
        }
        else
        {
            var sv = new ExpressionBuilder(options);

            try
            {
                expression = sv.CreateLambdaExpression(root);
                errorMessage = sv.FirstError;

                if (!options.AllowReflection && expression != null)
                {
                    var reflectionVisitor = new ReflectionDetector();
                    reflectionVisitor.Visit(expression);

                    if (reflectionVisitor.HasReflectionCall)
                    {
                        expression = null;
                        errorMessage = "Reflection is not allowed.";
                    }
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

    public static (IList<string> Identifiers, IList<string> UnknownIdentifiers) DetectIdentifiers(string expressionText, ExpressionParserOptions? options = null)
    {
        options ??= new ExpressionParserOptions();

        var root = PrepareCore(expressionText);
        var visitor = new IdentifierDetector(options);

        visitor.Visit(root);

        return (visitor.Identifiers, visitor.UnknownIdentifiers);
    }

    private static SyntaxNode PrepareCore(string expressionText)
    {
        if (string.IsNullOrWhiteSpace(expressionText))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(expressionText));

        var tree = CSharpSyntaxTree.ParseText(expressionText, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
        var root = tree.GetRoot();

        return root;
    }
}
