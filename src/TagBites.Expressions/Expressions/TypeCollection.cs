using TagBites.Utils;

namespace TagBites.Expressions;

internal class TypeCollection : Dictionary<string, Type>, ICollection<Type>
{
    public bool IsReadOnly { get; internal set; }
    public bool AllowStaticOnly { get; set; }

    public TypeCollection() { }
    public TypeCollection(IEqualityComparer<string> comparer) : base(comparer) { }


    public bool Contains(Type item) => TryGetValue(GetKey(item), out var t) && item == t;
    public void CopyTo(Type[] array, int arrayIndex) => Values.CopyTo(array, arrayIndex);

    public void Add(Type item)
    {
        if (IsReadOnly)
            throw new NotSupportedException("Collection is read-only.");

        if (AllowStaticOnly && (!item.IsAbstract || !item.IsSealed))
            throw new ArgumentException($"Type '{item.GetFriendlyTypeName()}' is not a static class and cannot be used as a static import.", nameof(item));

        var key = GetKey(item);

        if (TryGetValue(key, out var t))
            if (item != t)
                throw new ArgumentException($"Different type with the same name '{t.Name}' has already been included.");
            else
                return;

        Add(key, item);
    }
    public bool Remove(Type item)
    {
        if (IsReadOnly)
            throw new NotSupportedException("Collection is read-only.");

        return Remove(GetKey(item));
    }

    private static string GetKey(Type type)
    {
        var name = type.Name.Replace('`', '\'');
        return type is { IsGenericType: true, IsGenericTypeDefinition: false }
            ? GetKey(name, type.GetGenericArguments())
            : name;
    }
    internal static string GetKey(string arityName, Type[] genericArgumentTypes)
    {
        return arityName + "[" + string.Join(",", genericArgumentTypes.Select(x => x.FullName ?? x.Name)) + "]";
    }

    IEnumerator<Type> IEnumerable<Type>.GetEnumerator() => Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => Values.GetEnumerator();
}
