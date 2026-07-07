namespace TagBites.Expressions.Tests.Models;

internal class ParamsModel
{
    public int Sum(params int[] values) => values.Sum();
    public long SumLong(params long[] values) => values.Sum();

    public string Describe(string prefix) => prefix + ":none";
    public string Describe(string prefix, params int[] values) => prefix + ":" + values.Length;

    public int First(int[] values) => values[0];

    public static string Join(string separator, params string[] values) => string.Join(separator, values);
}
