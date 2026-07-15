// ReSharper disable UnusedMember.Local
namespace TagBites.Expressions.Tests;

public class DelegateMemberInvocationTests
{
    [Fact]
    public void DelegateProperty()
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(HasDelegateField), "m") }
        };

        var result = ExpressionParser.Invoke<int>("m.Doubler(21)", options, new HasDelegateField());

        Assert.Equal(42, result);
    }

    [Fact]
    public void DelegateProperty_WrongArgumentCount()
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(HasDelegateField), "m") }
        };

        var success = ExpressionParser.TryParse("m.Doubler(1, 2)", options, out _, out var error);

        Assert.False(success);
        Assert.Contains("Doubler", error);
    }

    [Fact]
    public void NonDelegateMember_NotFound()
    {
        var options = new ExpressionParserOptions
        {
            Parameters = { (typeof(HasDelegateField), "m") }
        };

        var success = ExpressionParser.TryParse("m.NotADelegate()", options, out _, out var error);

        Assert.False(success);
        Assert.Contains("not found", error);
    }

    private class HasDelegateField
    {
        public Func<int, int> Doubler { get; } = x => x * 2;
        public string NotADelegate => "";
    }
}
