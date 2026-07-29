using System.Linq.Expressions;
using Microsoft.CodeAnalysis;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    private Expression? DeclareVariable(SyntaxNode node, Expression expression, string name)
    {
        if (_variables?.Any(x => string.Equals(x.Name, name, _nameComparison)) == true
            || HasParameter(name)
            || _nestedParameters?.Any(x => string.Equals(x.Name, name, _nameComparison)) == true)
        {
            return ToError(node, $"Variable '{name}' is already declared.");
        }

        var index = _nextVariableIndex++;

        _variableContextParameter ??= Expression.Parameter(typeof(LambdaVariableContext), "__lvc_");
        _variables ??= [];
        _variables.Add((expression.Type, name, index));

        return Expression.Call(_variableContextParameter, s_lvcSetValue, Expression.Constant(index), ToCast(expression, typeof(object)));
    }

    private class LambdaVariableContext(int count)
    {
        private readonly object[] _values = new object[count];


        public T GetValue<T>(int index) => (T)_values[index];
        public void SetValue(int index, object value) => _values[index] = value;
    }
}
