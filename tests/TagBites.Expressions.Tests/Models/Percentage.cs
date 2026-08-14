namespace TagBites.Expressions.Tests.Models;

internal record struct Percentage(decimal Value)
{
    public static implicit operator decimal(Percentage percentage) => percentage.Value;
}
