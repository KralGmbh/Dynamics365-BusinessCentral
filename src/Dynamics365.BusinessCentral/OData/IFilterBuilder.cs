using System.Linq.Expressions;

namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// <see cref="Filter"/>'s operator set with the entity type already fixed, so a query does
/// not restate its type argument at every operator. Obtained inside
/// <c>Query&lt;T&gt;().Where(f =&gt; ...)</c>:
/// <code>
/// client.Query&lt;SalesOrder&gt;()
///     .Where(f =&gt; f.Equals(x =&gt; x.Status, "Open")
///                  .And(f.GreaterThan(x =&gt; x.Amount, 100)))
/// </code>
/// Every method forwards to the corresponding <see cref="Filter"/> overload and returns the
/// same <see cref="ODataFilter"/>, so <c>.And</c>/<c>.Or</c>/<c>.Not</c> composition and
/// rendering are identical to the static form.
/// </summary>
/// <typeparam name="TEntity">Entity type the enclosing query is bound to.</typeparam>
public interface IFilterBuilder<TEntity>
{
    /// <inheritdoc cref="Filter.Equals{TEntity}(Expression{Func{TEntity, object?}}, object?)"/>
    ODataFilter Equals(Expression<Func<TEntity, object?>> field, object? value);

    /// <inheritdoc cref="Filter.NotEquals{TEntity}(Expression{Func{TEntity, object?}}, object?)"/>
    ODataFilter NotEquals(Expression<Func<TEntity, object?>> field, object? value);

    /// <inheritdoc cref="Filter.GreaterThan{TEntity}(Expression{Func{TEntity, object?}}, object)"/>
    ODataFilter GreaterThan(Expression<Func<TEntity, object?>> field, object value);

    /// <inheritdoc cref="Filter.GreaterOrEqual{TEntity}(Expression{Func{TEntity, object?}}, object)"/>
    ODataFilter GreaterOrEqual(Expression<Func<TEntity, object?>> field, object value);

    /// <inheritdoc cref="Filter.LessThan{TEntity}(Expression{Func{TEntity, object?}}, object)"/>
    ODataFilter LessThan(Expression<Func<TEntity, object?>> field, object value);

    /// <inheritdoc cref="Filter.LessOrEqual{TEntity}(Expression{Func{TEntity, object?}}, object)"/>
    ODataFilter LessOrEqual(Expression<Func<TEntity, object?>> field, object value);

    /// <inheritdoc cref="Filter.Contains{TEntity}(Expression{Func{TEntity, object?}}, string)"/>
    ODataFilter Contains(Expression<Func<TEntity, object?>> field, string value);

    /// <inheritdoc cref="Filter.StartsWith{TEntity}(Expression{Func{TEntity, object?}}, string)"/>
    ODataFilter StartsWith(Expression<Func<TEntity, object?>> field, string value);

    /// <inheritdoc cref="Filter.EndsWith{TEntity}(Expression{Func{TEntity, object?}}, string)"/>
    ODataFilter EndsWith(Expression<Func<TEntity, object?>> field, string value);

    /// <inheritdoc cref="Filter.In{TEntity}(Expression{Func{TEntity, object?}}, object[])"/>
    ODataFilter In(Expression<Func<TEntity, object?>> field, params object[] values);

    /// <inheritdoc cref="Filter.In{TEntity}(Expression{Func{TEntity, object?}}, IEnumerable{object})"/>
    ODataFilter In(Expression<Func<TEntity, object?>> field, IEnumerable<object> values);

    /// <inheritdoc cref="Filter.IsNull{TEntity}(Expression{Func{TEntity, object?}})"/>
    ODataFilter IsNull(Expression<Func<TEntity, object?>> field);

    /// <inheritdoc cref="Filter.IsNotNull{TEntity}(Expression{Func{TEntity, object?}})"/>
    ODataFilter IsNotNull(Expression<Func<TEntity, object?>> field);

    /// <inheritdoc cref="Filter.All"/>
    ODataFilter All { get; }

    /// <inheritdoc cref="Filter.None"/>
    ODataFilter None { get; }
}

/// <summary>
/// The stateless forwarding implementation — one cached instance per closed generic.
/// </summary>
internal sealed class FilterBuilder<TEntity> : IFilterBuilder<TEntity>
{
    public static readonly FilterBuilder<TEntity> Instance = new();

    private FilterBuilder()
    {
    }

    public ODataFilter Equals(Expression<Func<TEntity, object?>> field, object? value) =>
        Filter.Equals(field, value);

    public ODataFilter NotEquals(Expression<Func<TEntity, object?>> field, object? value) =>
        Filter.NotEquals(field, value);

    public ODataFilter GreaterThan(Expression<Func<TEntity, object?>> field, object value) =>
        Filter.GreaterThan(field, value);

    public ODataFilter GreaterOrEqual(Expression<Func<TEntity, object?>> field, object value) =>
        Filter.GreaterOrEqual(field, value);

    public ODataFilter LessThan(Expression<Func<TEntity, object?>> field, object value) =>
        Filter.LessThan(field, value);

    public ODataFilter LessOrEqual(Expression<Func<TEntity, object?>> field, object value) =>
        Filter.LessOrEqual(field, value);

    public ODataFilter Contains(Expression<Func<TEntity, object?>> field, string value) =>
        Filter.Contains(field, value);

    public ODataFilter StartsWith(Expression<Func<TEntity, object?>> field, string value) =>
        Filter.StartsWith(field, value);

    public ODataFilter EndsWith(Expression<Func<TEntity, object?>> field, string value) =>
        Filter.EndsWith(field, value);

    public ODataFilter In(Expression<Func<TEntity, object?>> field, params object[] values) =>
        Filter.In(field, values);

    public ODataFilter In(Expression<Func<TEntity, object?>> field, IEnumerable<object> values) =>
        Filter.In(field, values);

    public ODataFilter IsNull(Expression<Func<TEntity, object?>> field) =>
        Filter.IsNull(field);

    public ODataFilter IsNotNull(Expression<Func<TEntity, object?>> field) =>
        Filter.IsNotNull(field);

    public ODataFilter All => Filter.All;

    public ODataFilter None => Filter.None;
}
