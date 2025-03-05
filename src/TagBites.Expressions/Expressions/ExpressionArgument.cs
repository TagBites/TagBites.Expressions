using System.Diagnostics;

namespace TagBites.Expressions;

[DebuggerDisplay("{Name} = {Value}")]
public readonly record struct ExpressionArgument
{
    private readonly Type? _type;

    public string Name { get; }
    public object? Value { get; }
    public Type Type => _type ?? Value?.GetType() ?? typeof(object);

    public ExpressionArgument(string name, object? value, Type? type = null)
    {
        Name = name;
        Value = value;
        _type = type;
    }


    public void Deconstruct(out string name, out object? value, out Type? type)
    {
        name = Name;
        value = Value;
        type = Type;
    }

    public static implicit operator ExpressionArgument((string Name, object? Value) item)
    {
        return new ExpressionArgument(item.Name, item.Value);
    }
    public static implicit operator ExpressionArgument((string Name, object? Value, Type? Type) item)
    {
        return new ExpressionArgument(item.Name, item.Value, item.Type);
    }
}
