using System.Linq.Expressions;

namespace TagBites.Expressions;

/// <summary>
/// Options for parsing expressions, including validation, parameters, global members, and custom property resolution.
/// </summary>
[PublicAPI]
public class ExpressionParserOptions
{
    private IList<(Type Type, string Name)> _parameters = new List<(Type, string)>();
    private IDictionary<string, (Type? Type, object? Value)> _globalMembers = new Dictionary<string, (Type? Type, object? Value)>();

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
        get => _parameters;
        set => _parameters = value ?? throw new ArgumentNullException(nameof(value));
    }
    /// <summary>
    /// List of global members (values or delegates).
    /// Member type is optional; if null, the type is based on the value.  
    /// If both member type and value are null, the type is object.  
    /// Member with name 'this' can be access implicitly (when <see cref="UseFirstParameterAsThis"/> is <c>false</c>).
    /// </summary>
    public IDictionary<string, (Type? Type, object? Value)> GlobalMembers
    {
        get => _globalMembers;
        set => _globalMembers = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// True if the first parameter should be used as 'this' so its members can be accessed implicitly.
    /// Alternatively, the 'this' member in <see cref="GlobalMembers"/> can be used.
    /// Default: <c>false</c>.
    /// </summary>
    public bool UseFirstParameterAsThis { get; set; }
    /// <summary>
    /// Indicates whether to use reduced expressions.
    /// Default: <c>true</c>.
    /// </summary>
    public bool UseReducedExpressions { get; set; }
    /// <summary>
    /// Indicates whether to allow reflection.
    /// Default: <c>false</c>.
    /// </summary>
    public bool AllowReflection { get; set; }
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
    /// Collection of types that can be used in expressions.
    /// </summary>
    public ICollection<Type> IncludedTypes { get; } = new TypeCollection();
    internal IDictionary<string, Type> IncludedTypesMap => (TypeCollection)IncludedTypes;

    /// <summary>
    /// Function to resolve properties dynamically at runtime based on the provided context.
    /// </summary>
    public Func<IExpressionMemberResolverContext, Expression?>? CustomPropertyResolver { get; set; }

    public ExpressionParserOptions()
    {
#if !DEBUG
        UseReducedExpressions = true;
#endif
    }

    private class TypeCollection : Dictionary<string, Type>, ICollection<Type>
    {
        public bool IsReadOnly => false;


        public bool Contains(Type item) => TryGetValue(item.Name, out var t) && item == t;
        public void CopyTo(Type[] array, int arrayIndex) => Values.CopyTo(array, arrayIndex);

        public void Add(Type item)
        {
            if (TryGetValue(item.Name, out var t))
                if (item != t)
                    throw new ArgumentException($"Different type with the same name '{t.Name}' has already been included.");
                else
                    return;

            Add(item.Name, item);
        }
        public bool Remove(Type item) => Remove(item.Name);

        IEnumerator<Type> IEnumerable<Type>.GetEnumerator() => Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Values.GetEnumerator();
    }
}
