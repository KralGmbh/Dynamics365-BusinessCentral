using System.Linq.Expressions;

namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// Resolves a property selector to the OData field name Business Central sees on the wire —
/// for consumers of the path-based API who want typed field names in <c>select:</c> lists
/// or raw filter strings without adopting the <c>Query&lt;T&gt;()</c> builder.
/// </summary>
/// <remarks>
/// Resolution is identical to the typed <see cref="Filter"/> overloads and to
/// deserialization: <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/>
/// first, then the shared naming policy. A hand-maintained constants class duplicating wire
/// names can be replaced by call-site resolution:
/// <code>
/// select: [BusinessCentralField.Of&lt;SalesOrder&gt;(o =&gt; o.No),
///          BusinessCentralField.Of&lt;SalesOrder&gt;(o =&gt; o.Amount)]
/// </code>
/// </remarks>
public static class BusinessCentralField
{
    /// <summary>
    /// Returns the OData field name for <paramref name="selector"/>. Nested selectors such
    /// as <c>o =&gt; o.Customer.Name</c> become navigation paths (<c>customer/name</c>).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="selector"/> is not a plain property access — method calls and
    /// computed expressions cannot be translated to OData.
    /// </exception>
    public static string Of<TEntity>(Expression<Func<TEntity, object?>> selector) =>
        PropertyPath.Resolve(selector);
}
