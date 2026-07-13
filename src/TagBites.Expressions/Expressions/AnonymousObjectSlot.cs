using System.Dynamic;

namespace TagBites.Expressions;

/// <summary>
/// Backing storage for anonymous types, to track distinct types in expression tree without generating a new type.
/// </summary>
internal abstract class AnonymousObject : DynamicObject, IDictionary<string, object>
{
    private static readonly Type[] s_digitTypes =
    [
        typeof(object), typeof(bool), typeof(char), typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int),
        typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(DateTime), typeof(string)
    ];

    private readonly Dictionary<string, object> _values = new();

    object IDictionary<string, object>.this[string key]
    {
        get => _values[key];
        set => _values[key] = value;
    }
    ICollection<string> IDictionary<string, object>.Keys => _values.Keys;
    ICollection<object> IDictionary<string, object>.Values => _values.Values;
    int ICollection<KeyValuePair<string, object>>.Count => _values.Count;
    bool ICollection<KeyValuePair<string, object>>.IsReadOnly => false;


    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        var found = _values.TryGetValue(binder.Name, out var value);
        result = value;
        return found;
    }
    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        _values[binder.Name] = value!;
        return true;
    }
    public override IEnumerable<string> GetDynamicMemberNames() => _values.Keys;

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
            return true;

        if (obj is not AnonymousObject other || other.GetType() != GetType() || other._values.Count != _values.Count)
            return false;

        foreach (var pair in _values)
        {
            if (!other._values.TryGetValue(pair.Key, out var otherValue) || !Equals(pair.Value, otherValue))
                return false;
        }

        return true;
    }
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = GetType().GetHashCode();

            foreach (var key in _values.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                hash = (hash * 397) ^ key.GetHashCode();
                hash = (hash * 397) ^ (_values[key]?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    void IDictionary<string, object>.Add(string key, object value) => _values.Add(key, value);
    void ICollection<KeyValuePair<string, object>>.Add(KeyValuePair<string, object> item) => ((IDictionary<string, object>)_values).Add(item);
    void ICollection<KeyValuePair<string, object>>.Clear() => _values.Clear();
    bool ICollection<KeyValuePair<string, object>>.Contains(KeyValuePair<string, object> item) => ((IDictionary<string, object>)_values).Contains(item);
    bool IDictionary<string, object>.ContainsKey(string key) => _values.ContainsKey(key);
    void ICollection<KeyValuePair<string, object>>.CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) => ((IDictionary<string, object>)_values).CopyTo(array, arrayIndex);
    bool IDictionary<string, object>.Remove(string key) => _values.Remove(key);
    bool ICollection<KeyValuePair<string, object>>.Remove(KeyValuePair<string, object> item) => ((IDictionary<string, object>)_values).Remove(item);
    bool IDictionary<string, object>.TryGetValue(string key, out object value) => _values.TryGetValue(key, out value!);
    IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();

    public static Type GetAnonymousObjectType(int index) => typeof(AnonymousObject<>).MakeGenericType(BuildTypeArgument(index));
    private static Type BuildTypeArgument(int index)
    {
        var length = s_digitTypes.Length;

        if (index < length)
            return s_digitTypes[index];

        var first = s_digitTypes[index % length];
        var rest = BuildTypeArgument(index / length);

        return typeof(Tuple<,>).MakeGenericType(first, rest);
    }
}

// ReSharper disable once UnusedTypeParameter
/// <summary> <inheritdoc/> </summary>
internal sealed class AnonymousObject<T> : AnonymousObject;
