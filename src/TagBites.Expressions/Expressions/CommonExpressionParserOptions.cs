using System.Collections.Concurrent;
using System.Reflection;
using MemberCacheKey = (TagBites.Expressions.MemberLookupKind Kind, System.Type Type, string Name, System.Reflection.BindingFlags Flags);

namespace TagBites.Expressions;

/// <summary>
/// Settings of an <see cref="ExpressionParserOptions"/> instance that stay fixed across its forks.
/// A single instance is shared by the source options and every fork, so the reflection member cache is shared too.
/// </summary>
internal sealed class CommonExpressionParserOptions : ExpressionBuilderOptions
{
    private ConcurrentDictionary<MemberCacheKey, MemberInfo[]>? _memberCache;
    private ConcurrentDictionary<MethodBase, (ParameterInfo[] Parameters, bool HasParams)>? _signatureCache;
    private ConcurrentDictionary<(Type Source, Type Target, string Name), MethodInfo?>? _conversionCache;

    public bool IgnoreCase;
    public bool UseMemberCache;

    public IDictionary<string, (Type? Type, object? Value)>? GlobalMembersMap;
    public TypeCollection? IncludedTypesMap;
    public TypeCollection? StaticImportsMap;

    public CommonExpressionParserOptions() { }
    public CommonExpressionParserOptions(CommonExpressionParserOptions other)
    {
        AllowReflection = other.AllowReflection;
        IgnoreCase = other.IgnoreCase;
        AllowRuntimeCast = other.AllowRuntimeCast;
        AllowStringRelationalOperators = other.AllowStringRelationalOperators;
        AllowThrowExpressions = other.AllowThrowExpressions;
        IgnoreBuiltInTypes = other.IgnoreBuiltInTypes;
        TypeResolver = other.TypeResolver;
        CustomPropertyResolver = other.CustomPropertyResolver;
        UseMemberCache = other.UseMemberCache;

        if (other.GlobalMembersMap is { Count: > 0 })
            GlobalMembersMap = new Dictionary<string, (Type? Type, object? Value)>(other.GlobalMembersMap);

        if (other.IncludedTypesMap is { Count: > 0 })
        {
            var copy = new TypeCollection();
            foreach (var type in other.IncludedTypesMap.Values)
                copy.Add(type);
            IncludedTypesMap = copy;
        }

        if (other.StaticImportsMap is { Count: > 0 })
        {
            var copy = new TypeCollection { AllowStaticOnly = true };
            foreach (var type in other.StaticImportsMap.Values)
                copy.Add(type);
            StaticImportsMap = copy;
        }
    }


    public ConcurrentDictionary<MemberCacheKey, MemberInfo[]> GetOrCreateMemberCache() => _memberCache ??= new ConcurrentDictionary<MemberCacheKey, MemberInfo[]>();
    public ConcurrentDictionary<MethodBase, (ParameterInfo[] Parameters, bool HasParams)> GetOrCreateSignatureCache() => _signatureCache ??= new ConcurrentDictionary<MethodBase, (ParameterInfo[], bool)>();
    public ConcurrentDictionary<(Type Source, Type Target, string Name), MethodInfo?> GetOrCreateConversionCache() => _conversionCache ??= new ConcurrentDictionary<(Type, Type, string), MethodInfo?>();
}
