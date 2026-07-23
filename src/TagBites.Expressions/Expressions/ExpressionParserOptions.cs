using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using MemberCacheKey = (TagBites.Expressions.MemberLookupKind Kind, System.Type Type, string Name, System.Reflection.BindingFlags Flags);

namespace TagBites.Expressions;

/// <summary>
/// Options for parsing expressions, including validation, parameters, global members, and custom property resolution.
/// </summary>
/// <remarks>
/// An instance becomes read-only the first time it is used for parsing.
/// </remarks>
[PublicAPI]
public class ExpressionParserOptions
{
    private readonly object _syncRoot = new();
    private ParserContext? _prepared;

    /// <summary>
    /// Expected and required result type of the expression.
    /// Used only for validation. If null, the result type is not checked.
    /// </summary>
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
    /// List of global members (values or delegates).
    /// Member type is optional; if null, the type is based on the value.
    /// If both member type and value are null, the type is object.
    /// Member with name 'this' can be access implicitly (when <see cref="UseFirstParameterAsThis"/> is <c>false</c>).
    /// </summary>
    public IDictionary<string, (Type? Type, object? Value)> GlobalMembers
    {
        get
        {
            GlobalMembersInternal ??= _prepared != null
                ? new ReadOnlyDictionary<string, (Type? Type, object? Value)>(new Dictionary<string, (Type? Type, object? Value)>())
                : new Dictionary<string, (Type? Type, object? Value)>();

            return GlobalMembersInternal;
        }
        set
        {
            CheckReadOnly();
            GlobalMembersInternal = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
    internal IDictionary<string, (Type? Type, object? Value)>? GlobalMembersInternal { get; private set; }

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
        get;
        set
        {
            CheckReadOnly();
            field = value;
        }
    }
    /// <summary>
    /// True to resolve parameters, variables, global members, type members and <see cref="IncludedTypes"/> case-insensitively.
    /// Default: <c>false</c>.
    /// </summary>
    public bool IgnoreCase
    {
        get;
        set
        {
            CheckReadOnly();
            field = value;
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
        get;
        set
        {
            CheckReadOnly();
            field = value;
        }
    }
    /// <summary>
    /// True to allow <c>&lt;</c> / <c>&lt;=</c> / <c>&gt;</c> / <c>&gt;=</c> on strings, compared ordinally via <see cref="string.Compare(string, string)"/>.
    /// Not valid in real C#.
    /// Default: <c>false</c>.
    /// </summary>
    public bool AllowStringRelationalOperators
    {
        get;
        set
        {
            CheckReadOnly();
            field = value;
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
        get;
        set
        {
            CheckReadOnly();
            field = value;
        }
    }
    /// <summary>
    /// Collection of types that can be used in expressions.
    /// </summary>
    public ICollection<Type> IncludedTypes => IncludedTypesMap ??= new TypeCollection { IsReadOnly = _prepared != null };
    internal TypeCollection? IncludedTypesMap { get; private set; }
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
        get;
        set
        {
            CheckReadOnly();
            field = value;
        }
    }

    /// <summary>
    /// Collection of types imported statically, as if <c>using static</c> was applied.
    /// Their public static methods, fields, properties and constants can be used unqualified.
    /// For example, adding <see cref="Math"/> makes <c>Sqrt(x)</c>, <c>Max(a, b)</c>, <c>PI</c> and <c>E</c> available.
    /// Members of instance parameters, global members and instance types always take precedence.
    /// </summary>
    public ICollection<Type> StaticImports => StaticImportsMap ??= new TypeCollection { AllowStaticOnly = true, IsReadOnly = _prepared != null };
    internal TypeCollection? StaticImportsMap { get; private set; }

    /// <summary>
    /// Function to resolve property/field-style access for types whose shape only exists at runtime,
    /// e.g. a database row, a CMS content type, a value that lives in another process.
    /// </summary>
    public Func<IExpressionMemberResolverContext, Expression?>? CustomPropertyResolver
    {
        get;
        set
        {
            CheckReadOnly();
            field = value;
        }
    }

    /// <summary>
    /// Caches reflected members (methods, indexers, extension methods) on this options instance.
    /// Default: <c>false</c>.
    /// </summary>
    public bool UseMemberCache
    {
        get;
        set
        {
            CheckReadOnly();
            field = value;
        }
    }
    internal ConcurrentDictionary<MemberCacheKey, MethodInfo[]>? MemberCache;

    public ExpressionParserOptions() { }
    /// <summary>
    /// Initializes a new mutable options instance by copying every setting from <paramref name="other"/>.
    /// </summary>
    public ExpressionParserOptions(ExpressionParserOptions other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        ResultType = other.ResultType;
        ResultCastType = other.ResultCastType;
        UseFirstParameterAsThis = other.UseFirstParameterAsThis;
        AllowReflection = other.AllowReflection;
        IgnoreCase = other.IgnoreCase;
        AllowRuntimeCast = other.AllowRuntimeCast;
        AllowStringRelationalOperators = other.AllowStringRelationalOperators;
        IgnoreBuiltInTypes = other.IgnoreBuiltInTypes;
        TypeResolver = other.TypeResolver;
        CustomPropertyResolver = other.CustomPropertyResolver;
        UseMemberCache = other.UseMemberCache;

        if (other.ParametersInternal is { Count: > 0 })
            ParametersInternal = new List<(Type, string)>(other.ParametersInternal);

        if (other.GlobalMembersInternal is { Count: > 0 })
            GlobalMembersInternal = new Dictionary<string, (Type? Type, object? Value)>(other.GlobalMembersInternal);

        if (other.IncludedTypesMap is { Count: > 0 })
        {
            var copy = new TypeCollection();
            foreach (var type in other.IncludedTypesMap.Values)
                copy.Add(type);
            IncludedTypesMap = copy;
        }

        if (other.StaticImportsMap is { Count: > 0 })
        {
            var copy = new TypeCollection { AllowStaticOnly = true };
            foreach (var type in other.StaticImportsMap.Values)
                copy.Add(type);
            StaticImportsMap = copy;
        }
    }


    internal ParserContext PrepareContext()
    {
        var prepared = _prepared;
        if (prepared != null)
            return (ParserContext)prepared;

        lock (_syncRoot)
        {
            if (_prepared != null)
                return (ParserContext)_prepared;

            // Freeze collections
            if (ParametersInternal is not null and not ReadOnlyCollection<(Type Type, string Name)>)
                ParametersInternal = new ReadOnlyCollection<(Type Type, string Name)>(ParametersInternal);

            if (GlobalMembersInternal is not null and not ReadOnlyDictionary<string, (Type? Type, object? Value)>)
                GlobalMembersInternal = new ReadOnlyDictionary<string, (Type? Type, object? Value)>(GlobalMembersInternal);

            IncludedTypesMap?.IsReadOnly = true;
            StaticImportsMap?.IsReadOnly = true;

            // Create
            var state = new ParserContext(this);
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
