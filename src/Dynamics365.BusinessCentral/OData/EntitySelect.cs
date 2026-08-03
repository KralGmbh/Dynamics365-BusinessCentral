using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// Derives the default <c>$select</c> list for an entity type: the wire names of the
/// columns the type can actually hold. Used by the fluent builder when a query specifies
/// neither <c>Select(...)</c> nor <c>SelectAll()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Inclusion is deliberately strict — every included name is sent to the server, and a
/// name that is not a real column fails the query with a <c>400</c>:
/// </para>
/// <list type="bullet">
/// <item>public instance properties, readable <b>and settable</b> (<c>set</c> or
/// <c>init</c>) — a get-only computed property cannot receive data and is not a column;</item>
/// <item>scalar types only (value types and <see cref="string"/>, including nullables) —
/// classes and collections are navigations, which belong in <c>$expand</c>;</item>
/// <item>not unconditionally <c>[JsonIgnore]</c> — conditional ignores such as
/// <c>WhenWritingNull</c> still deserialize, so those properties remain columns;</item>
/// <item>wire name not starting with <c>@</c> — <c>@odata.etag</c> and friends are
/// annotations the server sends regardless, not selectable columns.</item>
/// </list>
/// <para>
/// Names resolve through <see cref="PropertyPath.ResolveName"/> — the same rules as
/// filters, ordering and deserialization — and are sorted ordinally so the emitted URL is
/// deterministic.
/// </para>
/// <para>
/// <b>One way this can fail a query that previously succeeded.</b> The inclusion rules above
/// are the type's view of itself; nothing here can consult the tenant, so a property that
/// maps to <b>no Business Central column at all</b> is only discovered by the server
/// rejecting it. Such a property used to bind as its default and cost nothing; it now enters
/// <c>$select</c> and fails the whole request with a <c>400</c> before any row is read. This
/// is newly created breakage, and saying otherwise would be wrong. The shape to watch for is
/// an inherited base class of system fields applied to entity sets that do not all expose
/// them.
/// </para>
/// <para>
/// The remedy is per-property <c>[JsonIgnore]</c> or a query-level <c>SelectAll()</c>, and
/// <see cref="DerivedSelectHint"/> puts both into the exception message so the failure
/// explains itself. Probing <c>$metadata</c> once, when adopting this, catches every such
/// property at once instead of one <c>400</c> at a time.
/// </para>
/// <para>
/// <b>Casing is not a second cause.</b> Earlier releases documented <c>$select</c> as
/// case-sensitive server-side; measurement against a live Business Central SaaS tenant showed
/// otherwise — three spellings of one column all returned <c>200</c>, and the server answers
/// in its own canonical casing regardless of what was requested. A <c>[JsonPropertyName]</c>
/// whose casing disagrees with <c>$metadata</c> is therefore harmless here. The on-premises
/// OData stack is a different deployment and was not measured, so this is stated as "not
/// case-sensitive where measured", not as a guarantee.
/// </para>
/// </remarks>
internal static class EntitySelect
{
    private static readonly ConcurrentDictionary<Type, string[]> _cache = new();

    public static string[] For<TEntity>() => _cache.GetOrAdd(typeof(TEntity), static type =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Where(p => IsScalar(p.PropertyType))
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always })
            .Select(PropertyPath.ResolveName)
            .Where(name => !name.StartsWith('@'))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray());

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsValueType || underlying == typeof(string);
    }
}
