using System.Linq.Expressions;

namespace TagBites.Expressions;

/// <summary>
/// Provides data for a <see cref="ExpressionParserOptions.CustomPropertyResolver"/> call.
/// </summary>
/// <remarks>
/// The resolver is called for a member access on an instance (<c>instance.Member</c>).
/// Return the expression that reads the member, or <c>null</c> to let the parser resolve it in the usual way.
/// </remarks>
[PublicAPI]
public interface IExpressionMemberResolverContext
{
    /// <summary>
    /// Gets the expression the member is accessed on.
    /// </summary>
    Expression Instance { get; }
    /// <summary>
    /// Gets the type info attached to <see cref="Instance"/> by an earlier <see cref="IncludeTypeInfo"/> call, or <c>null</c> when there is none.
    /// </summary>
    /// <remarks>
    /// For an element of a sequence this is the type info of the sequence,
    /// so a lambda parameter keeps the info attached to the collection it iterates.
    /// </remarks>
    object? InstanceTypeInfo { get; }

    /// <summary>
    /// Gets the name of the accessed member.
    /// </summary>
    string MemberName { get; }
    /// <summary>
    /// Gets the full member path from the root of the expression (for example <c>this.Person.Name</c>), or <c>null</c> when the path is not known.
    /// </summary>
    string? MemberFullPath { get; }


    /// <summary>
    /// Returns the parameter of the resulting lambda with the given name.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <returns>The parameter expression.</returns>
    ParameterExpression GetParameter(string name);
    /// <summary>
    /// Attaches type info to an expression, so a later member access on it can read the info back through <see cref="InstanceTypeInfo"/>.
    /// </summary>
    /// <param name="expression">The expression to attach the info to.</param>
    /// <param name="typeInfo">The type info to attach.</param>
    /// <returns>The expression carrying the type info.</returns>
    Expression IncludeTypeInfo(Expression expression, object typeInfo);
}
