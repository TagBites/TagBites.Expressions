using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace TagBites.Expressions;

internal partial class ExpressionBuilder
{
    private (ParameterInfo[] Parameters, bool HasParams) GetSignature(MethodBase method)
    {
        if (_context.SignatureCache is not { } cache)
            return ComputeSignature(method);

        if (cache.TryGetValue(method, out var cached))
            return cached;

        var result = ComputeSignature(method);
        cache[method] = result;
        return result;
    }
    private static (ParameterInfo[] Parameters, bool HasParams) ComputeSignature(MethodBase method)
    {
        var parameters = method.GetParameters();
        var hasParams = parameters.Length > 0 && parameters[parameters.Length - 1].IsDefined(typeof(ParamArrayAttribute), false);
        return (parameters, hasParams);
    }

    private IList<MethodInfo> GetMethods(Type instanceType, string name, BindingFlags additionalFlags)
    {
        if (_context.MemberCache == null)
            return GetMethodsCore(instanceType, name, additionalFlags, _nameComparison);

        var key = (MemberLookupKind.Methods, instanceType, name, additionalFlags);

        if (_context.MemberCache.TryGetValue(key, out var cached))
            return (MethodInfo[])cached;

        var result = GetMethodsCore(instanceType, name, additionalFlags, _nameComparison);
        _context.MemberCache[key] = result;

        return result;
    }
    private static MethodInfo[] GetMethodsCore(Type instanceType, string name, BindingFlags additionalFlags, StringComparison comparison)
    {
        var members = new List<MethodInfo>();
        var names = new HashSet<string>();

        for (var type = instanceType; type != null; type = type.BaseType)
        {
            var nextMembers = type.GetMethods(BindingFlags.Public | additionalFlags);

            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var i = 0; i < nextMembers.Length; i++)
            {
                var item = nextMembers[i];
                if (string.Equals(item.Name, name, comparison) && names.Add(item.ToString()))
                    members.Add(item);
            }

            if (type == typeof(object))
                break;
        }

        foreach (var type in instanceType.GetInterfaces())
        {
            var nextMembers = type.GetMethods(BindingFlags.Public | additionalFlags);

            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var i = 0; i < nextMembers.Length; i++)
            {
                var item = nextMembers[i];
                if (string.Equals(item.Name, name, comparison) && names.Add(item.ToString()))
                    members.Add(item);
            }
        }

        if (instanceType.IsInterface)
        {
            var nextMembers = typeof(object).GetMethods(BindingFlags.Public | additionalFlags);

            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var i = 0; i < nextMembers.Length; i++)
            {
                var item = nextMembers[i];
                if (string.Equals(item.Name, name, comparison) && names.Add(item.ToString()))
                    members.Add(item);
            }
        }

        return members.ToArray();
    }

    private IList<MethodInfo> GetExtensionMethods(Type instanceType, string name)
    {
        if (_context.MemberCache == null)
            return GetExtensionMethodsCore(instanceType, name, _context.IncludedTypes, _nameComparison) ?? [];

        var key = (MemberLookupKind.ExtensionMethods, instanceType, name, default(BindingFlags));

        if (_context.MemberCache.TryGetValue(key, out var cached))
            return (MethodInfo[])cached;

        var result = GetExtensionMethodsCore(instanceType, name, _context.IncludedTypes, _nameComparison)?.ToArray() ?? [];
        _context.MemberCache[key] = result;

        return result;
    }
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(Enumerable))]
    private static IList<MethodInfo>? GetExtensionMethodsCore(Type instanceType, string name, TypeCollection? includedTypes, StringComparison comparison)
    {
        List<MethodInfo>? members = null;

        // From known extensions
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(instanceType))
            FindMembers(typeof(Enumerable));

        // From included types
        if (includedTypes?.Count > 0)
            foreach (var type in includedTypes.Values)
                if (type.IsAbstract && type.IsSealed && type != typeof(Enumerable))
                    FindMembers(type);

        return members;

        void FindMembers(Type type)
        {
            var nextMembers = type.GetMethods(BindingFlags.Public | BindingFlags.Static);

            // ReSharper disable once ForCanBeConvertedToForeach
            // ReSharper disable once LoopCanBeConvertedToQuery
            for (var i = 0; i < nextMembers.Length; i++)
            {
                var item = nextMembers[i];
                if (string.Equals(item.Name, name, comparison) && item.GetCustomAttribute<ExtensionAttribute>() != null)
                {
                    var thisParameter = item.GetParameters().FirstOrDefault();
                    if (thisParameter == null)
                        continue;

                    if (!IsMatchingParameterType(thisParameter.ParameterType, instanceType))
                        continue;

                    members ??= [];
                    members.Add(item);
                }
            }
        }
    }

    private IList<MethodInfo> GetIndexers(Type instanceType)
    {
        if (_context.MemberCache == null)
            return GetIndexersCore(instanceType);

        var key = (MemberLookupKind.Indexers, instanceType, "", default(BindingFlags));

        if (_context.MemberCache.TryGetValue(key, out var cached))
            return (MethodInfo[])cached;

        var result = GetIndexersCore(instanceType).ToArray();
        _context.MemberCache[key] = result;

        return result;
    }
    private static IList<MethodInfo> GetIndexersCore(Type instanceType)
    {
        var members = new List<MethodInfo>();

        Collect(instanceType);

        if (instanceType.IsInterface)
            foreach (var type in instanceType.GetInterfaces())
                Collect(type);

        return members;

        void Collect(Type type)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var getter = property.GetMethod;
                if (property.GetIndexParameters().Length == 0 || getter is not { IsPublic: true })
                    continue;

                if (!members.Any(x => HasSameParameterTypes(x, getter)))
                    members.Add(getter);
            }
        }
    }
    private PropertyInfo[] GetIndexerProperties(Type instanceType)
    {
        if (_context.MemberCache == null)
            return GetIndexerPropertiesCore(instanceType);

        var key = (MemberLookupKind.Properties, instanceType, "", default(BindingFlags));

        if (_context.MemberCache.TryGetValue(key, out var cached))
            return (PropertyInfo[])cached;

        var result = GetIndexerPropertiesCore(instanceType);
        _context.MemberCache[key] = result;

        return result;
    }
    private static PropertyInfo[] GetIndexerPropertiesCore(Type instanceType)
    {
        return Array.FindAll(instanceType.GetProperties(BindingFlags.Public | BindingFlags.Instance), x => x.GetIndexParameters().Length > 0);
    }

    private MemberInfo[] GetTypeMembers(Type instanceType, string name, bool includeInterfaces)
    {
        var flags = (includeInterfaces ? BindingFlags.FlattenHierarchy : default) | _context.CaseInsensitiveFlag;

        if (_context.MemberCache == null)
            return GetTypeMembersCore(instanceType, name, includeInterfaces, _context.CaseInsensitiveFlag);

        var key = (MemberLookupKind.Members, instanceType, name, flags);
        if (_context.MemberCache.TryGetValue(key, out var cached))
            return cached;

        var result = GetTypeMembersCore(instanceType, name, includeInterfaces, _context.CaseInsensitiveFlag);
        _context.MemberCache[key] = result;

        return result;
    }
    private static MemberInfo[] GetTypeMembersCore(Type instanceType, string name, bool includeInterfaces, BindingFlags caseFlag)
    {
        for (var type = instanceType; type != null; type = type.BaseType)
        {
            var members = type.GetMember(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly | BindingFlags.Public | caseFlag);
            if (members.Length > 0)
                return members;

            if (type == typeof(object))
                break;
        }

        if (includeInterfaces)
            foreach (var interfaceType in instanceType.GetInterfaces())
            {
                var members = interfaceType.GetMember(name, BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | caseFlag);
                if (members.Length > 0)
                    return members;
            }

        return [];
    }

    private static bool HasSameParameterTypes(MethodInfo a, MethodInfo b)
    {
        var pa = a.GetParameters();
        var pb = b.GetParameters();
        if (pa.Length != pb.Length)
            return false;

        for (var i = 0; i < pa.Length; i++)
            if (pa[i].ParameterType != pb[i].ParameterType)
                return false;

        return true;
    }
}
