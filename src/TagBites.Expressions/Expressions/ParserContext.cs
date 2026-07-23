using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using TagBites.Utils;
using MemberCacheKey = (TagBites.Expressions.MemberLookupKind Kind, System.Type Type, string Name, System.Reflection.BindingFlags Flags);

namespace TagBites.Expressions;

/// <summary>
/// Reusable lookup structures prepared once from an <see cref="ExpressionParserOptions"/> instance and shared across parses.
/// </summary>
internal readonly struct ParserContext
{
    public readonly ParameterExpression[] Parameters;
    public readonly Expression? ThisParameter;
    public readonly IDictionary<string, (Type? Type, object? Value)>? GlobalMembers;
    public readonly TypeCollection? IncludedTypes;
    public readonly TypeCollection? StaticImports;
    public readonly StringComparison NameComparison;
    public readonly BindingFlags CaseInsensitiveFlag;
    public readonly ConcurrentDictionary<MemberCacheKey, MethodInfo[]>? MemberCache;

    internal ParserContext(ExpressionParserOptions options)
    {
        // Case
        NameComparison = options.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        CaseInsensitiveFlag = options.IgnoreCase ? BindingFlags.IgnoreCase : default;

        // Parameters
        Parameters = options.ParametersInternal?.ToFastArray(x => Expression.Parameter(x.Type, x.Name)) ?? [];

        // Collections
        var globalMembers = options.GlobalMembersInternal;
        var includedTypes = options.IncludedTypesMap;
        var staticImports = options.StaticImportsMap;

        if (options.IgnoreCase)
        {
            if (globalMembers?.Count > 0)
            {
                var caseInsensitive = new Dictionary<string, (Type? Type, object? Value)>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in globalMembers)
                {
                    if (caseInsensitive.ContainsKey(item.Key))
                        throw new ArgumentException($"Duplicate case-insensitive global member name '{item.Key}'.", nameof(ExpressionParserOptions.GlobalMembers));

                    caseInsensitive.Add(item.Key, item.Value);
                }

                globalMembers = caseInsensitive;
            }

            if (includedTypes?.Count > 0)
            {
                var caseInsensitive = new TypeCollection(StringComparer.OrdinalIgnoreCase);

                foreach (var item in includedTypes)
                {
                    if (caseInsensitive.ContainsKey(item.Key))
                        throw new ArgumentException($"Duplicate case-insensitive type name '{item.Key}'.", nameof(ExpressionParserOptions.IncludedTypes));

                    caseInsensitive.Add(item.Key, item.Value);
                }

                includedTypes = caseInsensitive;
            }
        }

        GlobalMembers = globalMembers;
        IncludedTypes = includedTypes;
        StaticImports = staticImports;

        // This
        if (options.UseFirstParameterAsThis)
        {
            if (Parameters.Length > 0)
                ThisParameter = Parameters[0];
        }
        else if (globalMembers?.TryGetValue("this", out var item) == true && item.Value != null)
            ThisParameter = Expression.Constant(item.Value, ExpressionBuilder.GetGlobalMemberType("this", item));

        // Cache
        MemberCache = options.UseMemberCache
            ? options.MemberCache ??= new ConcurrentDictionary<MemberCacheKey, MethodInfo[]>()
            : null;
    }
}
