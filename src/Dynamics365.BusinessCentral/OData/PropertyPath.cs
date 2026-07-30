using Dynamics365.BusinessCentral.Options;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// Turns a property selector such as <c>o =&gt; o.Amount</c> into the OData field name.
/// </summary>
/// <remarks>
/// Names are resolved exactly the way <see cref="BusinessCentralJson"/> serializes them —
/// <see cref="JsonPropertyNameAttribute"/> first, then the configured naming policy. That
/// keeps <c>$filter</c>, <c>$select</c> and <c>$orderby</c> in agreement with
/// deserialization, which is the usual cause of "field not found" errors when field names
/// are typed by hand.
/// </remarks>
internal static class PropertyPath
{
    private static readonly ConcurrentDictionary<MemberInfo, string> _cache = new();

    /// <summary>
    /// Resolves a selector to an OData field name. Nested selectors such as
    /// <c>o =&gt; o.Customer.Name</c> become navigation paths (<c>customer/name</c>).
    /// </summary>
    public static string Resolve<TEntity>(Expression<Func<TEntity, object?>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var body = Unwrap(selector.Body);

        if (body is not MemberExpression member)
        {
            throw new ArgumentException(
                $"Expected a property selector such as x => x.Name, but got '{selector.Body}'. " +
                "Method calls and computed expressions cannot be translated to OData.",
                nameof(selector));
        }

        var segments = new List<string>();

        for (MemberExpression? current = member; current != null; current = Unwrap(current.Expression) as MemberExpression)
            segments.Insert(0, ResolveName(current.Member));

        return string.Join("/", segments);
    }

    /// <summary>Strips the boxing conversion the compiler inserts for value-typed properties.</summary>
    private static Expression? Unwrap(Expression? expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary
            ? unary.Operand
            : expression;

    /// <summary>
    /// Resolves a member to its wire name — <see cref="JsonPropertyNameAttribute"/> first,
    /// then the shared naming policy. Internal so <c>EntitySelect</c> derives
    /// <c>$select</c> lists through the exact same rules; a second implementation would
    /// drift.
    /// </summary>
    internal static string ResolveName(MemberInfo member) => _cache.GetOrAdd(member, static m =>
    {
        var attribute = m.GetCustomAttribute<JsonPropertyNameAttribute>(inherit: true);

        if (attribute != null)
            return attribute.Name;

        var policy = BusinessCentralJson.Options.PropertyNamingPolicy;

        return policy?.ConvertName(m.Name) ?? m.Name;
    });
}
