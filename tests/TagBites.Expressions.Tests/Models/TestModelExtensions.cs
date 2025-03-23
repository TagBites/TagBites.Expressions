namespace TagBites.Expressions.Tests.Models;

internal static class TestModelExtensions
{
    public static int GetValueExtension(this TestModel model, int value) => value;
    public static int GetValueUsingInterfaceExtension(this ITestModel model, int value) => value;
}
