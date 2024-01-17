using System.Linq.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TagBites.Expressions;

[PublicAPI]
public static class ExpressionParser
{
    public static LambdaExpression Parse(string script, ExpressionParserOptions? options)
    {
        return TryParse(script, options, out var expression, out var errorMessage)
            ? expression!
            : throw new ExpressionParserException(errorMessage!);
    }
    public static bool TryParse(string script, ExpressionParserOptions? options, out LambdaExpression? expression, out string? errorMessage)
    {
        options ??= new ExpressionParserOptions();

        var root = PrepareScript(script);
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

    public static (IList<string> Identifiers, IList<string> UnknownIdentifiers) DetectIdentifiers(string script, ExpressionParserOptions? options)
    {
        options ??= new ExpressionParserOptions();

        var root = PrepareScript(script);
        var visitor = new IdentifierDetector(options);

        visitor.Visit(root);

        return (visitor.Identifiers, visitor.UnknownIdentifiers);
    }

    private static SyntaxNode PrepareScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(script));

        var tree = CSharpSyntaxTree.ParseText(script, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
        var root = tree.GetRoot();

        return root;
    }
}
