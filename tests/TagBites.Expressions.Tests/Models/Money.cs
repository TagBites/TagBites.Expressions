namespace TagBites.Expressions.Tests.Models;

internal record struct Money(decimal Value)
{
    public static implicit operator Money(decimal value) => new(value);
    public static Money operator +(Money a, Money b) => new(a.Value + b.Value);
}
