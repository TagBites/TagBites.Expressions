namespace TagBites.Expressions.Tests.Models;

internal record struct Coin(decimal Amount)
{
    public static Coin operator +(Coin a, Coin b) => new(a.Amount + b.Amount);
    public static Coin operator *(Coin a, decimal factor) => new(a.Amount * factor);
    public static implicit operator decimal(Coin coin) => coin.Amount;
}
