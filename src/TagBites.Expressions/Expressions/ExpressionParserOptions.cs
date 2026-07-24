using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq.Expressions;

namespace TagBites.Expressions;

/// <summary>
/// Options for parsing expressions, including validation, parameters, global members, and custom property resolution.
/// </summary>
/// <remarks>
/// An instance becomes read-only the first time it is used for parsing.
/// Use <see cref="Fork"/> to reuse the prepared, shared settings while varying the result type or parameters.
/// </remarks>
[PublicAPI]
public class ExpressionParserOptions
{
    private ExpressionBuilderContext? _prepared;

    internal CommonExpressionParserOptions Common;

    /// <summary>
    /// Expected and required result type of the expression.
    /// Used only for validation. If null, the result type is not checked.
    /// </summary>
    /// <remarks>
    /// Can be overridden per fork through <see cref="Fork"/>, because it is not part of the shared cached context.
    /// </remarks>
    public Type? ResultType
    {
        get;
        set
        {
            CheckReadOnly();
            field = value;
        }
    }
    /// <summary>
    /// A type to convert the expression to, for example, to create a general lambda like Func&lt;object&gt;.
    /// If null, the result type is based on the expression.
    /// </summary>
    public Type? ResultCastType
    {
        get;
        set
        {
            CheckReadOnly();
            field = value;
        }
    }

    /// <summary>
    /// List of parameters of the function.
    /// </summary>
    public IList<(Type Type, string Name)> Parameters
    {
        get
        {
            ParametersInternal ??= _prepared != null
                ? []
                : new List<(Type, string)>();

            return ParametersInternal;
        }
        set
        {
            CheckReadOnly();
            ParametersInternal = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
    internal IList<(Type Type, string Name)>? ParametersInternal { get; private set; }
    /// <summary>
    /// True if the first parameter should be used as 'this' so its members can be accessed implicitly.
    /// Alternatively, the 'this' member in <see cref="GlobalMembers"/> can be used.
    /// Default: <c>false</c>.
    /// </summary>
    public bool UseFirstParameterAsThis
    {
        get;
        set
        {
            CheckReadOnly();
            field = value;
        }
    }

    /// <summary>
    /// Indicates whether to allow reflection.
    /// Default: <c>false</c>.
    /// </summary>
    public bool AllowReflection
    {
        get => Common.AllowReflection;
        set
        {
            CheckReadOnly();
            Common.AllowReflection = value;
        }
    }
    /// <summary>
    /// True to resolve parameters, variables, global members, type members and <see cref="IncludedTypes"/> case-insensitively.
    /// Default: <c>false</c>.
    /// </summary>
    public bool IgnoreCase
    {
        get => Common.IgnoreCase;
        set
        {
            CheckReadOnly();
            Common.IgnoreCase = value;
        }
    }
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
    public bool AllowRuntimeCast
    {
        get => Common.AllowRuntimeCast;
        set
        {
            CheckReadOnly();
            Common.AllowRuntimeCast = value;
        }
    }
    /// <summary>
    /// True to allow <c>&lt;</c> / <c>&lt;=</c> / <c>&gt;</c> / <c>&gt;=</c> on strings, compared ordinally via <see cref="string.Compare(string, string)"/>.
    /// Not valid in real C#.
    /// Default: <c>false</c>.
    /// </summary>
    public bool AllowStringRelationalOperators
    {
        get => Common.AllowStringRelationalOperators;
        set
        {
            CheckReadOnly();
            Common.AllowStringRelationalOperators = value;
        }
    }
    /// <summary>
    /// Caches reflected members (methods, indexers, extension methods) on this options instance.
    /// Default: <c>false</c>.
    /// </summary>
    public bool UseMemberCache
    {
        get => Common.UseMemberCache;
        set
        {
            CheckReadOnly();
            Common.UseMemberCache = value;
        }
    }

    /// <summary>
    /// List of global members (values or delegates).
    /// Member type is optional; if null, the type is based on the value.
    /// If both member type and value are null, the type is object.
    /// Member with name 'this' can be access implicitly (when <see cref="UseFirstParameterAsThis"/> is <c>false</c>).
    /// </summary>
    public IDictionary<string, (Type? Type, object? Value)> GlobalMembers
    {
        get
        {
            Common.GlobalMembersMap ??= _prepared != null
                ? new ReadOnlyDictionary<string, (Type? Type, object? Value)>(new Dictionary<string, (Type? Type, object? Value)>())
                : new Dictionary<string, (Type? Type, object? Value)>();

            return Common.GlobalMembersMap;
        }
        set
        {
            CheckReadOnly();
            Common.GlobalMembersMap = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>
    /// True to disable the fixed set of common framework types that are otherwise available by their short name regardless of <see cref="IncludedTypes"/>.
    /// Default: <c>false</c>.
    /// </summary>
    /// <remarks>
    /// The built-in types are for example:
    /// <see cref="TimeSpan"/>, <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="DateTimeKind"/>, <see cref="DayOfWeek"/>,
    /// <see cref="StringComparison"/>, <see cref="CultureInfo"/>, <see cref="MidpointRounding"/>, <see cref="Math"/>,
    /// <see cref="Enumerable"/>, <see cref="List{T}"/>, <see cref="Dictionary{TKey,TValue}"/>, <see cref="HashSet{T}"/>,
    /// and <see cref="Convert"/> and more typically used in an expressions.<br/>
    /// The C# primitive keywords (<c>int</c>, <c>string</c>, <c>bool</c>, etc.) are always available and are not affected by this option.
    /// </remarks>
    public bool IgnoreBuiltInTypes
    {
        get => Common.IgnoreBuiltInTypes;
        set
        {
            CheckReadOnly();
            Common.IgnoreBuiltInTypes = value;
        }
    }
    /// <summary>
    /// Collection of types that can be used in expressions.
    /// </summary>
    public ICollection<Type> IncludedTypes => Common.IncludedTypesMap ??= new TypeCollection { IsReadOnly = _prepared != null };
    /// <summary>
    /// Function that resolves a type from its name, invoked when a type cannot be found among
    /// <see cref="ResultType"/>, <see cref="Parameters"/>, <see cref="IncludedTypes"/> or the built-in types.
    /// The name may be namespace-qualified (e.g. <c>System.Text.StringBuilder</c>) when that form is used in expression.
    /// A generic type name is suffixed with an apostrophe and the number of type arguments (e.g. <c>List'1</c>, <c>Dictionary'2</c>),
    /// and the returned type must be the corresponding open generic definition (e.g. <c>typeof(List&lt;&gt;)</c>).
    /// Return <c>null</c> to indicate the name is not recognized.
    /// </summary>
    public Func<string, Type?>? TypeResolver
    {
        get => Common.TypeResolver;
        set
        {
            CheckReadOnly();
            Common.TypeResolver = value;
        }
    }

    /// <summary>
    /// Collection of types imported statically, as if <c>using static</c> was applied.
    /// Their public static methods, fields, properties and constants can be used unqualified.
    /// For example, adding <see cref="Math"/> makes <c>Sqrt(x)</c>, <c>Max(a, b)</c>, <c>PI</c> and <c>E</c> available.
    /// Members of instance parameters, global members and instance types always take precedence.
    /// </summary>
    public ICollection<Type> StaticImports => Common.StaticImportsMap ??= new TypeCollection { AllowStaticOnly = true, IsReadOnly = _prepared != null };

    /// <summary>
    /// Function to resolve property/field-style access for types whose shape only exists at runtime,
    /// e.g. a database row, a CMS content type, a value that lives in another process.
    /// </summary>
    public Func<IExpressionMemberResolverContext, Expression?>? CustomPropertyResolver
    {
        get => Common.CustomPropertyResolver;
        set
        {
            CheckReadOnly();
            Common.CustomPropertyResolver = value;
        }
    }

    public ExpressionParserOptions() => Common = new CommonExpressionParserOptions();
    /// <summary>
    /// Initializes a new mutable options instance by copying every setting from <paramref name="other"/>.
    /// </summary>
    public ExpressionParserOptions(ExpressionParserOptions other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        Common = new CommonExpressionParserOptions(other.Common);

        ResultType = other.ResultType;
        ResultCastType = other.ResultCastType;

        // Parameters
        if (other.ParametersInternal is { Count: > 0 })
            ParametersInternal = new List<(Type, string)>(other.ParametersInternal);

        UseFirstParameterAsThis = other.UseFirstParameterAsThis;
    }
    private ExpressionParserOptions(ExpressionParserOptions other, CommonExpressionParserOptions common)
    {
        Common = common;
    }


    /// <summary>
    /// Creates a read-only variant that reuses the prepared, shared settings of this instance
    /// (global members, included types, static imports, member cache and the resolution flags),
    /// while overriding the result type, parameters or 'this' handling.
    /// </summary>
    /// <param name="resultType">The result type for the fork, or <c>null</c> to inherit <see cref="ResultType"/>.</param>
    /// <param name="resultCastType">The result cast type for the fork, or <c>null</c> to inherit <see cref="ResultCastType"/>.</param>
    /// <param name="parameters">The parameters for the fork, or <c>null</c> to inherit <see cref="Parameters"/>.</param>
    /// <param name="useFirstParameterAsThis">The 'this' handling for the fork, or <c>null</c> to inherit <see cref="UseFirstParameterAsThis"/>.</param>
    /// <returns>
    /// A prepared, read-only options instance that shares the reusable lookups of this instance.
    /// When every argument is <c>null</c>, this instance is returned, because the fork would be identical.
    /// </returns>
    /// <remarks>
    /// The shared member cache and settings are reused by every fork.
    /// Only the parameters and 'this' handling rebuild the parameter-specific part of the context.
    /// Forking makes this instance read-only, because its shared settings are now shared with the fork.
    /// </remarks>
    public ExpressionParserOptions Fork(Type? resultType = null, Type? resultCastType = null, IList<(Type Type, string Name)>? parameters = null, bool? useFirstParameterAsThis = null)
    {
        var context = GetPrepareContext();

        if (resultType == null && resultCastType == null && parameters == null && useFirstParameterAsThis == null)
            return this;

        var fork = new ExpressionParserOptions(this, Common)
        {
            ResultType = resultType ?? ResultType,
            ResultCastType = resultCastType ?? ResultCastType
        };

        // Parameters
        if (parameters == null && useFirstParameterAsThis == null)
            fork._prepared = context;
        else
        {
            if (parameters != null)
                fork.ParametersInternal = parameters;
            else if (ParametersInternal is { Count: > 0 })
                fork.ParametersInternal = new List<(Type, string)>(ParametersInternal);

            fork.UseFirstParameterAsThis = useFirstParameterAsThis ?? UseFirstParameterAsThis;

            fork._prepared = new ExpressionBuilderContext(context, fork.ParametersInternal, fork.UseFirstParameterAsThis);
        }

        return fork;
    }

    internal ExpressionBuilderContext GetPrepareContext()
    {
        var prepared = _prepared;
        if (prepared != null)
            return (ExpressionBuilderContext)prepared;

        lock (Common)
        {
            if (_prepared != null)
                return (ExpressionBuilderContext)_prepared;

            // Freeze collections
            if (ParametersInternal is { } parameters and not ReadOnlyCollection<(Type Type, string Name)>)
                ParametersInternal = new ReadOnlyCollection<(Type Type, string Name)>(parameters);

            if (Common.GlobalMembersMap is { } members and not ReadOnlyDictionary<string, (Type? Type, object? Value)>)
                Common.GlobalMembersMap = new ReadOnlyDictionary<string, (Type? Type, object? Value)>(members);

            Common.IncludedTypesMap?.IsReadOnly = true;
            Common.StaticImportsMap?.IsReadOnly = true;

            // Prepare context
            var state = new ExpressionBuilderContext(this);
            _prepared = state;
            return state;
        }
    }

    private void CheckReadOnly()
    {
        if (_prepared != null)
            throw new InvalidOperationException("The options instance is read-only because it has already been used for parsing.");
    }

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
