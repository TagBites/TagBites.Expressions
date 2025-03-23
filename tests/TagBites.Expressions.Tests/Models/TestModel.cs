// ReSharper disable UnusedMember.Global
namespace TagBites.Expressions.Tests.Models;

internal class TestModel : ITestModel
{
    private TestModel? _child;
    private TestModel? _dynamiChild;

    public int Value { get; }
    public TestModel TimesTen => _child ??= new TestModel(Value * 10);

    public int Field1 { get; set; }
    public int Field2 { get; set; }

    public TestModel? NullChild => null;

    public TestModel(int value = 1) => Value = value;


    public object? GetValue(string member)
    {
        return member switch
        {
            "TimesTwo" => Value * 2,
            "TimesTen" => TimesTen,
            "TimesTwenty" => _dynamiChild ??= new TestModel(Value * 20),
            _ => null
        };
    }
    public static Type? GetMemberType(string member)
    {
        return member switch
        {
            "TimesTwo" => typeof(int),
            "TimesTen" => typeof(TestModel),
            "TimesTwenty" => typeof(TestModel),
            _ => null
        };
    }

    public T? ReturnForExactType<T>(object v) => v is T v1 ? v1 : default;
    public T ReturnIt<T>(T value) => value;

    public T? GetExactOrDefault<T>(object v, T? defaultValue = default) => v is T v1 ? v1 : defaultValue;

    public static void StaticVoidMethod(object _) { }
}
