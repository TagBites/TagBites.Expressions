using System.Linq.Expressions;

namespace TagBites.Expressions.Extensions;

internal class ConditionalAccessExpression : Expression
{
    private readonly Type _type;

    private Expression Instance { get; }
    private Expression Member { get; }

    public override Type Type => _type;
    public override bool CanReduce => true;
    public override ExpressionType NodeType => ExpressionType.Extension;

    public ConditionalAccessExpression(Expression instance, Expression member)
    {
        Instance = instance;
        Member = member;

        _type = member.Type;

        if (_type.IsValueType && Nullable.GetUnderlyingType(_type) == null && _type != typeof(void))
            _type = typeof(Nullable<>).MakeGenericType(_type);
    }


    public override Expression Reduce()
    {
        var instanceVariable = Variable(Instance.Type, "instance");
        var member = new ReplaceExpressionVisitor(Instance, instanceVariable).Visit(Member)!;

        if (_type != member.Type)
            member = Convert(member, _type);

        Expression nullResult = _type == typeof(void)
            ? Empty()
            : Constant(null, _type);

        return Block(
            [instanceVariable],
            Assign(instanceVariable, Instance),
            Condition(
                ExpressionBuilder.ToIsNotNull(instanceVariable),
                member,
                nullResult));
    }
    public override string ToString()
    {
        var instance = Instance.ToString();
        var member = Member.ToString();

        if (member.StartsWith(instance + "."))
            return $"{instance}?{member.Substring(instance.Length)}";

        return $"({instance != null} ? {member} : default)";
    }
}
