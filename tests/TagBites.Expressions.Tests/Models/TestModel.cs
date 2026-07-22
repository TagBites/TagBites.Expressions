// ReSharper disable UnusedMember.Global
#pragma warning disable CA1822
namespace TagBites.Expressions.Tests.Models;

internal class TestModel(int value = 1) : ITestModel
{
    public int Value { get; } = value;
    public int Property1 { get; set; }
    public int Property2 { get; set; }

    public TestModel? ChildNull => null;
    public TestModel ChildTimesTen => field ??= new TestModel(Value * 10);


    public object? GetValue(string member)
    {
        return member switch
        {
            "TimesTwo" => Value * 2,
            "ChildTimesTen" => ChildTimesTen,
            _ => null
        };
    }

    public int Sum(int a, int b) => a + b;
    public int Subtract(int a, int b) => a - b;
    public string Concat3(string a, string b = "-", string c = "!") => a + b + c;
    public string Echo(string s) => s ?? "<null>";

    public string Overloaded(int a) => "int";
    public string Overloaded(string a) => "string";

    public string Widen(int a, int b) => "int";
    public string Widen(long a, long b) => "long";

    public T ReturnArgument<T>(T value) => value;
    public T? ReturnArgumentExactTypeOrNull<T>(object v) => v is T v1 ? v1 : default;
    public T? ReturnArgumentExactTypeOrDefault<T>(object v, T? defaultValue = default) => v is T v1 ? v1 : defaultValue;

    public static void StaticVoidMethod(object _) { }
}
