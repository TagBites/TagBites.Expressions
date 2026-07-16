namespace TagBites.Expressions;

internal class TypeCollection : Dictionary<string, Type>, ICollection<Type>
{
    public bool IsReadOnly { get; internal set; }


    public bool Contains(Type item) => TryGetValue(item.Name, out var t) && item == t;
    public void CopyTo(Type[] array, int arrayIndex) => Values.CopyTo(array, arrayIndex);

    public void Add(Type item)
    {
        if (IsReadOnly)
            throw new NotSupportedException("Collection is read-only.");

        if (TryGetValue(item.Name, out var t))
            if (item != t)
                throw new ArgumentException($"Different type with the same name '{t.Name}' has already been included.");
            else
                return;

        Add(item.Name, item);
    }
    public bool Remove(Type item)
    {
        if (IsReadOnly)
            throw new NotSupportedException("Collection is read-only.");

        return Remove(item.Name);
    }

    IEnumerator<Type> IEnumerable<Type>.GetEnumerator() => Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => Values.GetEnumerator();
}
