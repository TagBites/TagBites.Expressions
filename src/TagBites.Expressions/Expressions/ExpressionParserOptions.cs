using System.Linq.Expressions;

namespace TagBites.Expressions;

[PublicAPI]
public class ExpressionParserOptions
{
    /// <summary>
    /// Expected and required result type of the expression.
    /// </summary>
    public Type? ResultType { get; set; }
    /// <summary>
    /// A type to convert expression to, for example to create general lambda like Func&lt;object&gt;.
    /// </summary>
    public Type? ResultCastType { get; set; }

    public IList<(Type Type, string Name)> Parameters { get; } = new List<(Type, string)>();
    public bool UseFirstParameterAsThis { get; set; }
    public bool UseReducedExpressions { get; set; }
    public bool AllowReflection { get; set; }
    public bool AllowRuntimeCast { get; set; }

    public ICollection<Type> IncludedTypes { get; } = new TypeCollection();
    internal IDictionary<string, Type> IncludedTypesMap => (TypeCollection)IncludedTypes;
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
