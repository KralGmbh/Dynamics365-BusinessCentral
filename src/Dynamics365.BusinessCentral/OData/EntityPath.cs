using System.Collections.Concurrent;
using System.Reflection;

namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// Resolves the OData entity set path for a CLR type from
/// <see cref="BusinessCentralEntityAttribute"/>.
/// </summary>
internal static class EntityPath
{
    private static readonly ConcurrentDictionary<Type, string> _cache = new();

    /// <summary>
    /// Returns the path declared by <typeparamref name="TEntity"/>, or throws with an
    /// actionable message when the type is not annotated.
    /// </summary>
    public static string For<TEntity>() => For(typeof(TEntity));

    /// <inheritdoc cref="For{TEntity}"/>
    public static string For(Type type) => _cache.GetOrAdd(type, static t =>
    {
        var attribute = t.GetCustomAttribute<BusinessCentralEntityAttribute>(inherit: true)
            ?? throw new InvalidOperationException(
                $"Type '{t.Name}' has no [BusinessCentralEntity] attribute, so its OData path " +
                $"cannot be inferred. Either annotate it — [BusinessCentralEntity(\"salesOrders\")] — " +
                $"or pass the path explicitly, e.g. client.Query<{t.Name}>(\"salesOrders\").");

        return attribute.Path;
    });
}
