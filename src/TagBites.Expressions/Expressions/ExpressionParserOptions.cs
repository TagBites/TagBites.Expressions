using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using MemberCacheKey = (TagBites.Expressions.MemberLookupKind Kind, System.Type Type, string Name, System.Reflection.BindingFlags Flags);

namespace TagBites.Expressions;

/// <summary>
/// Options for parsing expressions, including validation, parameters, global members, and custom property resolution.
/// </summary>
[PublicAPI]
public class ExpressionParserOptions
{
    /// <summary>
    /// Expected and required result type of the expression.
    /// Used only for validation. If null, the result type is not checked.
    /// </summary>
    public Type? ResultType { get; set; }
    /// <summary>
    /// A type to convert the expression to, for example, to create a general lambda like Func&lt;object&gt;.
    /// If null, the result type is based on the expression.
    /// </summary>
    public Type? ResultCastType { get; set; }

    /// <summary>
    /// List of parameters of the function.
    /// </summary>
    public IList<(Type Type, string Name)> Parameters
    {
        get => ParametersInternal ??= new List<(Type, string)>();
        set => ParametersInternal = value ?? throw new ArgumentNullException(nameof(value));
    }
    internal IList<(Type Type, string Name)>? ParametersInternal { get; private set; }

    /// <summary>
    /// List of global members (values or delegates).
    /// Member type is optional; if null, the type is based on the value.  
    /// If both member type and value are null, the type is object.  
    /// Member with name 'this' can be access implicitly (when <see cref="UseFirstParameterAsThis"/> is <c>false</c>).
    /// </summary>
    public IDictionary<string, (Type? Type, object? Value)> GlobalMembers
    {
        get => GlobalMembersInternal ??= new Dictionary<string, (Type? Type, object? Value)>();
        set => GlobalMembersInternal = value ?? throw new ArgumentNullException(nameof(value));
    }
    internal IDictionary<string, (Type? Type, object? Value)>? GlobalMembersInternal { get; private set; }

    /// <summary>
    /// True if the first parameter should be used as 'this' so its members can be accessed implicitly.
    /// Alternatively, the 'this' member in <see cref="GlobalMembers"/> can be used.
    /// Default: <c>false</c>.
    /// </summary>
    public bool UseFirstParameterAsThis { get; set; }
    /// <summary>
    /// Indicates whether to allow reflection.
    /// Default: <c>false</c>.
    /// </summary>
    public bool AllowReflection { get; set; }
    /// <summary>
    /// True to resolve parameters, variables, global members, type members and <see cref="IncludedTypes"/> case-insensitively.
    /// Default: <c>false</c>.
    /// </summary>
    public bool IgnoreCase { get; set; }
    /// <summary>
    /// True to allow runtime casting using custom operators.
    /// Default: <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Custom operators:
    /// <code>typeis(someExpression, "MyNamespace.MyType,MyAssembly")</code>
    /// <code>typeas(someExpression, "MyNamespace.MyType,MyAssembly")</code>
    /// <code>typecast(someExpression, "MyNamespace.MyType,MyAssembly")</code>
    /// </remarks>
    public bool AllowRuntimeCast { get; set; }
    /// <summary>
    /// True to allow <c>&lt;</c> / <c>&lt;=</c> / <c>&gt;</c> / <c>&gt;=</c> on strings, compared ordinally via <see cref="string.Compare(string, string)"/>.
    /// Not valid in real C#.
    /// Default: <c>false</c>.
    /// </summary>
    public bool AllowStringRelationalOperators { get; set; }

    /// <summary>
    /// Collection of types that can be used in expressions.
    /// </summary>
    public ICollection<Type> IncludedTypes => IncludedTypesMap;
    internal TypeCollection IncludedTypesMap { get; } = new();

    /// <summary>
    /// Collection of types imported statically, as if <c>using static</c> was applied.
    /// Their public static methods, fields, properties and constants can be used unqualified.
    /// For example, adding <see cref="Math"/> makes <c>Sqrt(x)</c>, <c>Max(a, b)</c>, <c>PI</c> and <c>E</c> available.
    /// Members of instance parameters, global members and instance types always take precedence.
    /// </summary>
    public ICollection<Type> StaticImports => StaticImportsMap ??= new TypeCollection { AllowStaticOnly = true };
    internal TypeCollection? StaticImportsMap { get; private set; }

    /// <summary>
    /// Function to resolve property/field-style access for types whose shape only exists at runtime,
    /// e.g. a database row, a CMS content type, a value that lives in another process. 
    /// </summary>
    public Func<IExpressionMemberResolverContext, Expression?>? CustomPropertyResolver { get; set; }

    /// <summary>
    /// Caches reflected members (methods, indexers, extension methods) on this options instance.
    /// <see cref="IncludedTypes"/> and <see cref="StaticImports"/> becomes immutable after the first call.
    /// Default: <c>false</c>.
    /// </summary>
    public bool UseMemberCache { get; set; }
    internal Dictionary<MemberCacheKey, MethodInfo[]>? MemberCache;


    /// <summary>
    /// Now parser produces only standard expression. No reduce is needed.
    /// </summary>
    [Obsolete, EditorBrowsable(EditorBrowsableState.Never)]
    public bool UseReducedExpressions
    {
        get => true;
        // ReSharper disable once ValueParameterNotUsed
        set { }
    }
}
