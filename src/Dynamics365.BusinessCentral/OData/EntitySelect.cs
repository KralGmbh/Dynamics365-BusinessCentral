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
/// deterministic. Note that <c>$select</c> is case-<b>sensitive</b> on the server even
/// though deserialization is not: a <c>[JsonPropertyName]</c> whose casing drifts from
/// <c>$metadata</c> deserializes fine today but fails loudly here. That is this feature
/// surfacing latent drift, not creating it.
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
