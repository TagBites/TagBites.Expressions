namespace TagBites.Expressions.Tests.Models;

internal class RuntimeDefinedTypeInstance
{
    public RuntimeDefinedType Type { get; }
    private readonly Dictionary<string, object?> _values = new();

    public object? this[string propertyName]
    {
        get => _values.GetValueOrDefault(propertyName);
        set
        {
            var type = Type.Properties.GetValueOrDefault(propertyName);
            if (type == null || value?.GetType() is { } t && !type.IsAssignableFrom(t))
                throw new ArgumentException();

            _values[propertyName] = value;
        }
    }

    public RuntimeDefinedTypeInstance(RuntimeDefinedType type) => Type = type;


    public T? GetTypedValue<T>(string propertyName) => this[propertyName] is T t ? t : default;
}
