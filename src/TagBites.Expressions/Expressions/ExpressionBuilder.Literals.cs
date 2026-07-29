using System.Linq.Expressions;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TagBites.Expressions.Extensions;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    public override Expression VisitLiteralExpression(LiteralExpressionSyntax node)

    {

        if (node.IsKind(SyntaxKind.DefaultLiteralExpression))

            return new DelayDefaultExpression();



        return Expression.Constant(node.Token.Value);

    }

    public override Expression? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)

    {

        var format = new StringBuilder();

        var args = new List<Expression>();



        foreach (var content in node.Contents)

        {

            switch (content)

            {

                case InterpolatedStringTextSyntax item:

                    format.Append(item.TextToken.ValueText);

                    break;



                case InterpolationSyntax item:

                    var expression = Visit(item.Expression);

                    if (expression == null)

                        return null;



                    var formatText = item.FormatClause != null

                        ? ":" + item.FormatClause.FormatStringToken.ValueText

                        : null;

                    format.Append($"{{{args.Count}{item.AlignmentClause?.ToString()}{formatText}}}");

                    args.Add(ToCast(expression, typeof(object)));

                    break;



                default:

                    return ToError(content);

            }

        }



        return Expression.Call(

            null,

            s_stringFormat,

            [

                Expression.Constant(format.ToString()),

                Expression.NewArrayInit(typeof(object), args)

            ]);

    }
}
