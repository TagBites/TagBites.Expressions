using System.Reflection;
using System.Text;

namespace TagBites.Utils;

internal static class TypeUtils
{
    #region Types

    public static bool IsNumericType(Type type)
    {
        if (type.IsEnum)
            return false;

        var code = GetTypeCodeWithNullable(type);
        return code >= TypeCode.SByte && code <= TypeCode.Decimal;
    }
    private static TypeCode GetTypeCodeWithNullable(Type type)
    {
        if (type.IsEnum)
            type = Enum.GetUnderlyingType(type);

        return Type.GetTypeCode(type);
    }

    public static string? GetTypeAlias(Type type)
    {
        return type.FullName switch
        {
            "System.Boolean" => "bool",

            "System.Byte" => "byte",
            "System.SByte" => "sbyte",
            "System.Int16" => "short",
            "System.UInt16" => "ushort",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Int64" => "long",
            "System.UInt64" => "ulong",

            "System.Single" => "float",
            "System.Double" => "double",
            "System.Decimal" => "decimal",

            "System.Char" => "char",
            "System.String" => "string",

            "System.Object" => "object",
            "System.Void" => "void",

            _ => null
        };
    }
    public static string GetFriendlyTypeName(this Type type)
    {
        if (!type.IsGenericType)
            return GetTypeAlias(type) ?? type.Name;

        if (type.IsValueType)
        {
            var nullableType = Nullable.GetUnderlyingType(type);
            if (nullableType != null)
                return GetFriendlyTypeName(nullableType) + "?";
        }

        var types = type.GetGenericArguments();

        var sb = new StringBuilder();
        sb.Append(type.Name.Substring(0, type.Name.LastIndexOf('`')));
        sb.Append('<');
        for (var i = 0; i < types.Length; i++)
        {
            if (i > 0)
                sb.Append(',');

            sb.Append(GetFriendlyTypeName(types[i]));
        }
        sb.Append('>');

        return sb.ToString();
    }

    #endregion

    #region Generics

    public static bool ContainsGenericDefinition(Type type, Type genericTypeDefinition)
    {
        return GetGenericArguments(type, genericTypeDefinition).Length > 0;
    }
    public static Type[] GetGenericArguments(Type type, Type genericTypeDefinition)
    {
        var ti = type;

        if (ti.IsGenericTypeDefinition && type == genericTypeDefinition)
            return ti.GenericTypeArguments;

        if (ti.IsInterface)
        {
            if (ti.IsGenericType && type.GetGenericTypeDefinition() == genericTypeDefinition)
                return ti.GenericTypeArguments;
        }
        else
        {
            for (var it = ti; it != null; it = it.BaseType)
                if (it.IsGenericType && it.GetGenericTypeDefinition() == genericTypeDefinition)
                    return it.GenericTypeArguments;
        }

        foreach (var item in ti.GetInterfaces())
        {
            if (item.IsGenericType && item.GetGenericTypeDefinition() == genericTypeDefinition)
                return item.GenericTypeArguments;
        }

        return Array.Empty<Type>();
    }

    #endregion

    #region Properties

    public static PropertyInfo? GetProperty(object obj, string name, bool nonPublic)
    {
        return GetProperty(obj.GetType(), name, nonPublic, false);
    }
    public static PropertyInfo? GetProperty(Type type, string name, bool nonPublic, bool isStatic)
    {
        while (type != typeof(object) && type != null!)
        {
            var ti = type.GetTypeInfo();
            var property = ti.GetDeclaredProperty(name);
            if (property != null && property.GetMethod != null && (isStatic == property.GetMethod.IsStatic) && (nonPublic || property.GetMethod.IsPublic) && property.GetIndexParameters().Length == 0)
                return property;

            type = ti.BaseType!;
        }

        return null;
    }

    #endregion
}
