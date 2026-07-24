using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using TagBites.Utils;
using MemberCacheKey = (TagBites.Expressions.MemberLookupKind Kind, System.Type Type, string Name, System.Reflection.BindingFlags Flags);

namespace TagBites.Expressions;

/// <summary>
/// Reusable lookup structures prepared once from an <see cref="ExpressionParserOptions"/> instance and shared across parses.
/// The shared settings come from <see cref="CommonExpressionParserOptions"/>; only the parameters and 'this' handling are per instance.
/// </summary>
internal readonly struct ExpressionBuilderContext
{
    public readonly ParameterExpression[] Parameters;
    public readonly Expression? ThisParameter;
    public readonly IDictionary<string, (Type? Type, object? Value)>? GlobalMembers;
    public readonly TypeCollection? IncludedTypes;
    public readonly TypeCollection? StaticImports;
    public readonly StringComparison NameComparison;
    public readonly BindingFlags CaseInsensitiveFlag;
    public readonly ConcurrentDictionary<MemberCacheKey, MethodInfo[]>? MemberCache;

    internal ExpressionBuilderContext(ExpressionParserOptions options)
    {
        var common = options.Common;

        // Case
        NameComparison = common.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        CaseInsensitiveFlag = common.IgnoreCase ? BindingFlags.IgnoreCase : default;

        // Collections
        var globalMembers = common.GlobalMembersMap;
        var includedTypes = common.IncludedTypesMap;
        var staticImports = common.StaticImportsMap;

        if (common.IgnoreCase)
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

        // Cache
        MemberCache = common.UseMemberCache ? common.GetOrCreateMemberCache() : null;

        // Parameters
        Parameters = options.ParametersInternal?.ToFastArray(x => Expression.Parameter(x.Type, x.Name)) ?? [];

        // This
        if (options.UseFirstParameterAsThis)
        {
            if (Parameters.Length > 0)
                ThisParameter = Parameters[0];
        }
        else if (globalMembers?.TryGetValue("this", out var item) == true && item.Value != null)
            ThisParameter = Expression.Constant(item.Value, ExpressionBuilder.GetGlobalMemberType("this", item));
    }
    internal ExpressionBuilderContext(ExpressionBuilderContext other, IList<(Type Type, string Name)>? parameters, bool useFirstParameterAsThis)
    {
        NameComparison = other.NameComparison;
        CaseInsensitiveFlag = other.CaseInsensitiveFlag;

        GlobalMembers = other.GlobalMembers;
        IncludedTypes = other.IncludedTypes;
        StaticImports = other.StaticImports;

        MemberCache = other.MemberCache;

        // Parameters
        Parameters = parameters != null
            ? parameters.ToFastArray(x => Expression.Parameter(x.Type, x.Name))
            : other.Parameters;

        // This
        if (useFirstParameterAsThis)
        {
            if (Parameters.Length > 0)
                ThisParameter = Parameters[0];
        }
        else
        {
            if (GlobalMembers?.TryGetValue("this", out var item) == true && item.Value != null)
                ThisParameter = Expression.Constant(item.Value, ExpressionBuilder.GetGlobalMemberType("this", item));
        }
    }
}
