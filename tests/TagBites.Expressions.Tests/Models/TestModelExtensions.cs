namespace TagBites.Expressions.Tests.Models;

internal static class TestModelExtensions
{
    public static int GetValueExtension(this TestModel model, int value) => value;
    public static int GetValueUsingInterfaceExtension(this ITestModel model, int value) => value;

    public static T GenericFirstExtension<T>(this IEnumerable<T> source) where T : ITestModel => source.First();
    public static T GenericFirstExtension<T>(this IEnumerable<T> source, T other) where T : ITestModel => other;
    public static T GenericDictionaryExtension<TKey, T>(this IDictionary<TKey, T> source) where T : ITestModel where TKey : notnull => source.Values.First();
}
