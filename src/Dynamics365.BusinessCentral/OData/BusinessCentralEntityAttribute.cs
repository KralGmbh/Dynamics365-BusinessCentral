namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// Binds a CLR type to its Business Central OData entity set, so queries can be written
/// without repeating the path string at every call site.
/// </summary>
/// <example>
/// <code>
/// [BusinessCentralEntity("salesOrders")]
/// public sealed class SalesOrder { }
///
/// // path is inferred
/// var orders = await client.Query&lt;SalesOrder&gt;().ToListAsync();
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class BusinessCentralEntityAttribute : Attribute
{
    /// <summary>Relative OData entity set path, e.g. <c>salesOrders</c>.</summary>
    public string Path { get; }

    /// <summary>Binds the decorated type to <paramref name="path"/>.</summary>
    /// <param name="path">Relative OData entity set path.</param>
    public BusinessCentralEntityAttribute(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Entity path must not be empty.", nameof(path));

        Path = path;
    }
}
