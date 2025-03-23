using System.Collections.ObjectModel;

namespace TagBites.Expressions.Tests.Models;

internal class RuntimeDefinedTypeInstanceCollection : Collection<RuntimeDefinedTypeInstance>
{
    public RuntimeDefinedType Type { get; }

    public RuntimeDefinedTypeInstanceCollection(RuntimeDefinedType type) => Type = type;


    protected override void InsertItem(int index, RuntimeDefinedTypeInstance item)
    {
        if (item.Type != Type)
            throw new ArgumentException();

        base.InsertItem(index, item);
    }
    protected override void SetItem(int index, RuntimeDefinedTypeInstance item)
    {
        if (item.Type != Type)
            throw new ArgumentException();

        base.SetItem(index, item);
    }
}
