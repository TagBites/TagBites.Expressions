using System.Reflection;
using System.Text;

namespace TagBites.Utils;

internal static class TypeUtils
{
    #region Types

    private static readonly HashSet<(TypeCode From, TypeCode To)> s_implicitNumericConversions = new()
    {
        (TypeCode.Char, TypeCode.UInt16), (TypeCode.Char, TypeCode.Int32), (TypeCode.Char, TypeCode.UInt32),
        (TypeCode.Char, TypeCode.Int64), (TypeCode.Char, TypeCode.UInt64), (TypeCode.Char, TypeCode.Single),
        (TypeCode.Char, TypeCode.Double), (TypeCode.Char, TypeCode.Decimal),

        (TypeCode.SByte, TypeCode.Int16), (TypeCode.SByte, TypeCode.Int32), (TypeCode.SByte, TypeCode.Int64),
        (TypeCode.SByte, TypeCode.Single), (TypeCode.SByte, TypeCode.Double), (TypeCode.SByte, TypeCode.Decimal),

        (TypeCode.Byte, TypeCode.Int16), (TypeCode.Byte, TypeCode.UInt16), (TypeCode.Byte, TypeCode.Int32),
        (TypeCode.Byte, TypeCode.UInt32), (TypeCode.Byte, TypeCode.Int64), (TypeCode.Byte, TypeCode.UInt64),
        (TypeCode.Byte, TypeCode.Single), (TypeCode.Byte, TypeCode.Double), (TypeCode.Byte, TypeCode.Decimal),

        (TypeCode.Int16, TypeCode.Int32), (TypeCode.Int16, TypeCode.Int64), (TypeCode.Int16, TypeCode.Single),
        (TypeCode.Int16, TypeCode.Double), (TypeCode.Int16, TypeCode.Decimal),

        (TypeCode.UInt16, TypeCode.Int32), (TypeCode.UInt16, TypeCode.UInt32), (TypeCode.UInt16, TypeCode.Int64),
        (TypeCode.UInt16, TypeCode.UInt64), (TypeCode.UInt16, TypeCode.Single), (TypeCode.UInt16, TypeCode.Double),
        (TypeCode.UInt16, TypeCode.Decimal),

        (TypeCode.Int32, TypeCode.Int64), (TypeCode.Int32, TypeCode.Single), (TypeCode.Int32, TypeCode.Double),
        (TypeCode.Int32, TypeCode.Decimal),

        (TypeCode.UInt32, TypeCode.Int64), (TypeCode.UInt32, TypeCode.UInt64), (TypeCode.UInt32, TypeCode.Single),
        (TypeCode.UInt32, TypeCode.Double), (TypeCode.UInt32, TypeCode.Decimal),

        (TypeCode.Int64, TypeCode.Single), (TypeCode.Int64, TypeCode.Double), (TypeCode.Int64, TypeCode.Decimal),

        (TypeCode.UInt64, TypeCode.Single), (TypeCode.UInt64, TypeCode.Double), (TypeCode.UInt64, TypeCode.Decimal),

        (TypeCode.Single, TypeCode.Double),
    };


    public static bool IsNumericType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
            return false;

        var code = Type.GetTypeCode(type);
        return code is >= TypeCode.Char and <= TypeCode.Decimal;
    }
    private static TypeCode GetTypeCodeWithNullable(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
            type = Enum.GetUnderlyingType(type);

        return Type.GetTypeCode(type);
    }

    public static bool HasImplicitNumericConversion(Type from, Type to)
    {
        if (!IsNumericType(from) || !IsNumericType(to))
            return false;

        var fromCode = GetTypeCodeWithNullable(from);
        var toCode = GetTypeCodeWithNullable(to);

        return fromCode == toCode || s_implicitNumericConversions.Contains((fromCode, toCode));
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
