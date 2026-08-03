using System.Diagnostics;

namespace TagBites.Expressions;

/// <summary>
/// A named argument passed to an expression, with an optional declared type.
/// </summary>
[DebuggerDisplay("{Name} = {Value}")]
public readonly record struct ExpressionArgument
{
    private readonly Type? _type;

    /// <summary>
    /// Gets the argument name, used to reference the value in an expression.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the argument value.
    /// </summary>
    public object? Value { get; }
    /// <summary>
    /// Gets the declared type of the argument. Falls back to the runtime type of <see cref="Value"/>, or <see cref="object"/> when the value is <c>null</c>.
    /// </summary>
    public Type Type => _type ?? Value?.GetType() ?? typeof(object);

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpressionArgument"/> struct.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="value">The argument value.</param>
    /// <param name="type">The declared type. Pass <c>null</c> to infer it from <paramref name="value"/>.</param>
    public ExpressionArgument(string name, object? value, Type? type = null)
    {
        Name = name;
        Value = value;
        _type = type;
    }


    /// <summary>
    /// Deconstructs the argument into its name, value and type.
    /// </summary>
    /// <param name="name">The argument name.</param>
    /// <param name="value">The argument value.</param>
    /// <param name="type">The argument type.</param>
    public void Deconstruct(out string name, out object? value, out Type? type)
    {
        name = Name;
        value = Value;
        type = Type;
    }

    /// <summary>
    /// Creates an argument from a name and value tuple, inferring the type from the value.
    /// </summary>
    /// <param name="item">The name and value.</param>
    public static implicit operator ExpressionArgument((string Name, object? Value) item)
    {
        return new ExpressionArgument(item.Name, item.Value);
    }
    /// <summary>
    /// Creates an argument from a name, value and type tuple.
    /// </summary>
    /// <param name="item">The name, value and declared type.</param>
    public static implicit operator ExpressionArgument((string Name, object? Value, Type? Type) item)
    {
        return new ExpressionArgument(item.Name, item.Value, item.Type);
    }
}
