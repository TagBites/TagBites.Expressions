namespace TagBites.Expressions.Tests;

public class EnumOperatorTests
{
    private static ExpressionParserOptions Options() => new() { IncludedTypes = { typeof(TypeCode) } };

    [Fact]
    public void EnumPlusInt() => Assert.Equal(TypeCode.Boolean + 1, ExpressionParser.Invoke("TypeCode.Boolean + 1", Options()));

    [Fact]
    public void IntPlusEnum() => Assert.Equal(1 + TypeCode.Boolean, ExpressionParser.Invoke("1 + TypeCode.Boolean", Options()));

    [Fact]
    public void EnumMinusInt() => Assert.Equal(TypeCode.Boolean - 1, ExpressionParser.Invoke("TypeCode.Boolean - 1", Options()));

    [Fact]
    public void EnumMinusEnum() => Assert.Equal(TypeCode.Char - TypeCode.Boolean, ExpressionParser.Invoke("TypeCode.Char - TypeCode.Boolean", Options()));

    [Fact]
    public void EnumEqualEnum() => Assert.Equal(TypeCode.Boolean == TypeCode.Char, ExpressionParser.Invoke("TypeCode.Boolean == TypeCode.Char", Options()));

    [Fact]
    public void EnumLessThanEnum() => Assert.Equal(TypeCode.Boolean < TypeCode.Char, ExpressionParser.Invoke("TypeCode.Boolean < TypeCode.Char", Options()));

    [Fact]
    public void EnumAndEnum() => Assert.Equal(TypeCode.Boolean & TypeCode.Char, ExpressionParser.Invoke("TypeCode.Boolean & TypeCode.Char", Options()));

    [Fact]
    public void EnumOrEnum() => Assert.Equal(TypeCode.Boolean | TypeCode.Char, ExpressionParser.Invoke("TypeCode.Boolean | TypeCode.Char", Options()));

    [Fact]
    public void EnumXorEnum() => Assert.Equal(TypeCode.Boolean ^ TypeCode.Char, ExpressionParser.Invoke("TypeCode.Boolean ^ TypeCode.Char", Options()));

    [Fact]
    public void EnumEqualsZeroLiteral() => Assert.Equal(TypeCode.Empty == 0, ExpressionParser.Invoke("TypeCode.Empty == 0", Options()));

    [Fact]
    public void EnumNotEqualsZeroLiteral() => Assert.Equal(TypeCode.Boolean != 0, ExpressionParser.Invoke("TypeCode.Boolean != 0", Options()));

    [Fact]
    public void IntMinusEnum_NotValid() => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse("1 - TypeCode.Boolean", Options()));

    [Fact]
    public void EnumTimesInt_NotValid() => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse("TypeCode.Boolean * 1", Options()));

    [Fact]
    public void EnumEqualsNonZeroLiteral_NotValid() => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse("TypeCode.Boolean == 3", Options()));

    [Fact]
    public void DifferentEnumTypes_NotValid() => Assert.ThrowsAny<Exception>(() => ExpressionParser.Parse("TypeCode.Boolean == DayOfWeek.Monday", new ExpressionParserOptions { IncludedTypes = { typeof(TypeCode), typeof(DayOfWeek) } }));
}
