// ReSharper disable UnusedMember.Global
namespace TagBites.Expressions.Tests.Models;

internal class NamedArgModel
{
    public string Signature { get; }

    public NamedArgModel(int a, int b, int c = 100) => Signature = $"{a},{b},{c}";
    public NamedArgModel(string text) => Signature = $"text:{text}";

    // Two-parameter indexer, for named-argument reordering.
    public int this[int row, int col] => row * 10 + col;
}
