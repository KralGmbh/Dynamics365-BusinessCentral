using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.OData;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Dynamics365.BusinessCentral.Testing;

/// <summary>
/// Checks that every entity type's derived <c>$select</c> resolves against a tenant's real
/// <c>$metadata</c>.
/// </summary>
/// <remarks>
/// <para>
/// The fluent builder derives <c>$select</c> from an entity type's settable scalar properties.
/// Nothing in the package can consult a tenant's schema, so a property that maps to no Business
/// Central column is discovered only when the server rejects the whole query with a <c>400</c>.
/// Nothing between "upgrade" and "production" catches that on its own: mocks do not validate
/// <c>$select</c>, and neither does <see cref="FakeBusinessCentral"/> — a transport fake proves
/// what OData you generate, never what the tenant accepts.
/// </para>
/// <para>
/// This closes that gap, and closes it at the right moment. The failure is introduced by
/// <i>adding a property</i> — an edit nobody associates with a query breaking — so it wants a
/// check that runs on every build, not a migration step someone performs once.
/// </para>
/// <example>
/// One assertion, typically in an integration-test project pointed at a non-production tenant:
/// <code>
/// await BusinessCentralMetadata.AssertProjectionsResolveAsync(client, typeof(Item).Assembly);
/// </code>
/// </example>
/// <para>
/// The fetch is the only part that needs a tenant. <see cref="Parse"/> and <see cref="Validate"/>
/// are pure and can be exercised against a canned document, which is where the logic lives.
/// </para>
/// </remarks>
public static class BusinessCentralMetadata
{
    /// <summary>
    /// Fetches <c>$metadata</c>, checks every <c>[BusinessCentralEntity]</c> type in
    /// <paramref name="assembly"/>, and throws
    /// <see cref="BusinessCentralProjectionException"/> listing <b>every</b> problem if any
    /// projection fails to resolve.
    /// </summary>
    /// <param name="client">A configured client for the tenant to validate against.</param>
    /// <param name="assembly">The assembly to scan for annotated entity types.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BusinessCentralProjectionException">At least one projection did not resolve.</exception>
    public static async Task AssertProjectionsResolveAsync(
        IBusinessCentralClient client,
        Assembly assembly,
        CancellationToken cancellationToken = default)
    {
        var report = await ValidateAssemblyAsync(client, assembly, cancellationToken)
            .ConfigureAwait(false);

        if (!report.IsValid)
            throw new BusinessCentralProjectionException(report);
    }

    /// <summary>
    /// As <see cref="AssertProjectionsResolveAsync(IBusinessCentralClient, Assembly, CancellationToken)"/>,
    /// for an explicit set of entity types rather than a whole assembly.
    /// </summary>
    /// <param name="client">A configured client for the tenant to validate against.</param>
    /// <param name="entityTypes">The entity types to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="BusinessCentralProjectionException">At least one projection did not resolve.</exception>
    public static async Task AssertProjectionsResolveAsync(
        IBusinessCentralClient client,
        IEnumerable<Type> entityTypes,
        CancellationToken cancellationToken = default)
    {
        var report = await ValidateAsync(client, entityTypes, cancellationToken).ConfigureAwait(false);

        if (!report.IsValid)
            throw new BusinessCentralProjectionException(report);
    }

    /// <summary>
    /// As <see cref="ValidateAssemblyAsync"/>, for an explicit set of entity types.
    /// </summary>
    /// <param name="client">A configured client for the tenant to validate against.</param>
    /// <param name="entityTypes">The entity types to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<BusinessCentralProjectionReport> ValidateAsync(
        IBusinessCentralClient client,
        IEnumerable<Type> entityTypes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(entityTypes);

        var xml = await client.GetMetadataAsync(cancellationToken).ConfigureAwait(false);

        return Validate(Parse(xml), entityTypes);
    }

    /// <summary>
    /// As <see cref="AssertProjectionsResolveAsync(IBusinessCentralClient, Assembly, CancellationToken)"/>,
    /// but returns the report instead of throwing — for callers that want to log or filter
    /// rather than fail.
    /// </summary>
    /// <param name="client">A configured client for the tenant to validate against.</param>
    /// <param name="assembly">The assembly to scan for annotated entity types.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<BusinessCentralProjectionReport> ValidateAssemblyAsync(
        IBusinessCentralClient client,
        Assembly assembly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(assembly);

        var xml = await client.GetMetadataAsync(cancellationToken).ConfigureAwait(false);

        return Validate(Parse(xml), EntityTypesIn(assembly));
    }

    /// <summary>
    /// Finds every type in <paramref name="assembly"/> carrying
    /// <see cref="BusinessCentralEntityAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Tolerates a partially loadable assembly: types whose dependencies are missing are
    /// skipped rather than aborting the scan, since one unloadable unrelated type should not
    /// stop the check.
    /// </remarks>
    /// <param name="assembly">The assembly to scan.</param>
    public static IReadOnlyList<Type> EntityTypesIn(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        Type?[] types;

        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }

        return [.. types
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t!.GetCustomAttribute<BusinessCentralEntityAttribute>() is not null)
            .Select(t => t!)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Parses an EDMX <c>$metadata</c> document into entity set → column names.
    /// </summary>
    /// <remarks>
    /// Matches on local element names, ignoring the EDMX and EDM namespaces, so it is not tied
    /// to one OData or Business Central schema version. Navigation properties are excluded:
    /// they belong to <c>$expand</c>, and <see cref="EntitySelect"/> never derives them.
    /// </remarks>
    /// <param name="metadataXml">The raw document, as returned by <c>GetMetadataAsync</c>.</param>
    public static BusinessCentralMetadataModel Parse(string metadataXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataXml);

        var root = XDocument.Parse(metadataXml).Root
            ?? throw new ArgumentException("The $metadata document is empty.", nameof(metadataXml));

        var all = root.Descendants().ToList();

        // EntityType name → its column names. Keyed by the bare name and, where a schema
        // namespace is present, by "Namespace.Name" — EntitySet references use the qualified
        // form, but a document may omit the namespace.
        var columnsByType = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entityType in all.Where(e => e.Name.LocalName == "EntityType"))
        {
            var name = entityType.Attribute("Name")?.Value;

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var columns = entityType
                .Elements()
                .Where(e => e.Name.LocalName == "Property")
                .Select(e => e.Attribute("Name")?.Value)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            columnsByType[name] = columns;

            var ns = entityType.Ancestors()
                .FirstOrDefault(a => a.Name.LocalName == "Schema")?
                .Attribute("Namespace")?.Value;

            if (!string.IsNullOrWhiteSpace(ns))
                columnsByType[$"{ns}.{name}"] = columns;
        }

        var sets = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var set in all.Where(e => e.Name.LocalName == "EntitySet"))
        {
            var setName = set.Attribute("Name")?.Value;
            var typeRef = set.Attribute("EntityType")?.Value;

            if (string.IsNullOrWhiteSpace(setName))
                continue;

            if (typeRef is not null && columnsByType.TryGetValue(typeRef, out var columns))
            {
                sets[setName] = columns;
                continue;
            }

            // Unqualified fallback: some documents reference the type by bare name.
            var bare = typeRef?.Split('.').LastOrDefault();

            sets[setName] = bare is not null && columnsByType.TryGetValue(bare, out var byBare)
                ? byBare
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return new BusinessCentralMetadataModel(sets);
    }

    /// <summary>
    /// Checks each type's derived projection against <paramref name="metadata"/>, collecting
    /// every problem rather than stopping at the first.
    /// </summary>
    /// <remarks>
    /// Column matching is <b>case-insensitive</b>, which is not laxness: <c>$select</c> was
    /// measured case-insensitive on Business Central SaaS, and the server answers in its own
    /// canonical casing regardless of what was requested. Matching ordinally here would report
    /// working projections as broken — precisely the false alarm this tool exists to avoid.
    /// </remarks>
    /// <param name="metadata">The parsed tenant schema.</param>
    /// <param name="entityTypes">The entity types to check.</param>
    public static BusinessCentralProjectionReport Validate(
        BusinessCentralMetadataModel metadata,
        IEnumerable<Type> entityTypes)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(entityTypes);

        var checkedTypes = new List<Type>();
        var skipped = new List<Type>();
        var problems = new List<BusinessCentralProjectionProblem>();

        foreach (var type in entityTypes)
        {
            var path = EntityPath.For(type);

            // A navigation path names no single entity set, so there is nothing to resolve
            // against. Recorded rather than silently dropped.
            if (path.Contains('/', StringComparison.Ordinal))
            {
                skipped.Add(type);
                continue;
            }

            checkedTypes.Add(type);

            if (!metadata.TryGetColumns(path, out var columns))
            {
                problems.Add(new BusinessCentralProjectionProblem
                {
                    EntityType = type,
                    EntitySet = path,
                    Kind = BusinessCentralProjectionProblemKind.UnknownEntitySet
                });

                continue;
            }

            foreach (var wireName in EntitySelect.For(type))
            {
                if (columns.Contains(wireName))
                    continue;

                problems.Add(new BusinessCentralProjectionProblem
                {
                    EntityType = type,
                    EntitySet = path,
                    Kind = BusinessCentralProjectionProblemKind.UnknownColumn,
                    Property = wireName,
                    DeclaringProperty = DeclaringPropertyFor(type, wireName)
                });
            }
        }

        return new BusinessCentralProjectionReport
        {
            Checked = checkedTypes,
            Skipped = skipped,
            Problems = problems
        };
    }

    /// <summary>
    /// Maps a wire name back to the CLR property that produced it, so a report names the
    /// member the developer has to edit rather than only the name on the wire.
    /// </summary>
    private static string? DeclaringPropertyFor(Type type, string wireName)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attributeName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;

            if (string.Equals(attributeName ?? property.Name, wireName, StringComparison.OrdinalIgnoreCase))
                return property.Name;

            // The derived name may also come from the naming policy rather than the attribute.
            if (attributeName is null &&
                string.Equals(property.Name, wireName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Name;
            }
        }

        return null;
    }
}

/// <summary>A tenant's <c>$metadata</c>, reduced to what projection validation needs.</summary>
public sealed class BusinessCentralMetadataModel
{
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _sets;

    internal BusinessCentralMetadataModel(IReadOnlyDictionary<string, IReadOnlySet<string>> sets) =>
        _sets = sets;

    /// <summary>Entity set names found in the document.</summary>
    public IReadOnlyCollection<string> EntitySets => (IReadOnlyCollection<string>)_sets.Keys;

    /// <summary>Looks up an entity set's column names. Set names match case-insensitively.</summary>
    /// <param name="entitySet">The entity set name, e.g. <c>salesOrders</c>.</param>
    /// <param name="columns">The set's column names, when found.</param>
    public bool TryGetColumns(string entitySet, out IReadOnlySet<string> columns) =>
        _sets.TryGetValue(entitySet, out columns!);
}
