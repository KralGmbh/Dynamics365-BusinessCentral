using System.Text;

namespace Dynamics365.BusinessCentral.Testing;

/// <summary>Why a derived projection failed to resolve against a tenant's <c>$metadata</c>.</summary>
public enum BusinessCentralProjectionProblemKind
{
    /// <summary>
    /// The entity set named by <c>[BusinessCentralEntity]</c> is not in the tenant's
    /// <c>$metadata</c> — every query against this type would 404.
    /// </summary>
    UnknownEntitySet,

    /// <summary>
    /// A derived <c>$select</c> name matches no column on the entity set. Under the fluent
    /// builder's default projection this fails the whole query with a <c>400</c>.
    /// </summary>
    UnknownColumn
}

/// <summary>One resolution failure, scoped to a single entity type.</summary>
public sealed class BusinessCentralProjectionProblem
{
    /// <summary>The entity class whose projection failed to resolve.</summary>
    public required Type EntityType { get; init; }

    /// <summary>The entity set the type maps to, from <c>[BusinessCentralEntity]</c>.</summary>
    public required string EntitySet { get; init; }

    /// <summary>What went wrong.</summary>
    public required BusinessCentralProjectionProblemKind Kind { get; init; }

    /// <summary>
    /// The offending wire name, for <see cref="BusinessCentralProjectionProblemKind.UnknownColumn"/>;
    /// <see langword="null"/> when the entity set itself is missing.
    /// </summary>
    public string? Property { get; init; }

    /// <summary>The CLR property that produced <see cref="Property"/>, when known.</summary>
    public string? DeclaringProperty { get; init; }

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        BusinessCentralProjectionProblemKind.UnknownEntitySet =>
            $"{EntityType.Name}: entity set '{EntitySet}' is not in $metadata.",

        _ => $"{EntityType.Name}.{DeclaringProperty ?? Property}: '{Property}' is not a column " +
             $"on '{EntitySet}'. Mark it [JsonIgnore], or call SelectAll() on queries for this type."
    };
}

/// <summary>
/// The result of checking every entity type's derived <c>$select</c> against a tenant's
/// <c>$metadata</c>.
/// </summary>
/// <remarks>
/// Reports <b>every</b> problem rather than stopping at the first, which is the whole point:
/// the alternative is discovering the same information one production <c>400</c> at a time.
/// </remarks>
public sealed class BusinessCentralProjectionReport
{
    /// <summary>Entity types that were checked.</summary>
    public required IReadOnlyList<Type> Checked { get; init; }

    /// <summary>
    /// Types skipped because their entity path is a navigation path (contains <c>/</c>) and
    /// therefore names no single entity set to resolve against.
    /// </summary>
    public required IReadOnlyList<Type> Skipped { get; init; }

    /// <summary>Every resolution failure found, in discovery order.</summary>
    public required IReadOnlyList<BusinessCentralProjectionProblem> Problems { get; init; }

    /// <summary>Whether every checked projection resolved.</summary>
    public bool IsValid => Problems.Count == 0;

    /// <summary>
    /// A multi-line summary suitable for a test failure message: a headline count, then one
    /// line per problem.
    /// </summary>
    public string Describe()
    {
        if (IsValid)
        {
            return $"All {Checked.Count} derived projection(s) resolved against $metadata" +
                   (Skipped.Count > 0 ? $" ({Skipped.Count} skipped)." : ".");
        }

        var sb = new StringBuilder()
            .Append(Problems.Count)
            .Append(Problems.Count == 1 ? " projection problem" : " projection problems")
            .Append(" across ")
            .Append(Checked.Count)
            .AppendLine(" entity type(s):")
            .AppendLine();

        foreach (var problem in Problems)
            sb.Append("  - ").AppendLine(problem.ToString());

        if (Skipped.Count > 0)
        {
            sb.AppendLine()
              .Append("  (skipped, navigation paths: ")
              .Append(string.Join(", ", Skipped.Select(t => t.Name)))
              .AppendLine(")");
        }

        return sb.ToString();
    }
}

/// <summary>
/// Thrown by
/// <see cref="BusinessCentralMetadata.AssertProjectionsResolveAsync(Dynamics365.BusinessCentral.Client.IBusinessCentralClient, System.Reflection.Assembly, CancellationToken)"/>
/// when at least one derived projection does not resolve.
/// </summary>
public sealed class BusinessCentralProjectionException : Exception
{
    /// <summary>The full report, so a caller can inspect problems rather than parse the message.</summary>
    public BusinessCentralProjectionReport Report { get; }

    /// <summary>Creates the exception from a failing report.</summary>
    /// <param name="report">The report describing every problem found.</param>
    public BusinessCentralProjectionException(BusinessCentralProjectionReport report)
        : base(report?.Describe() ?? "Projection validation failed.")
    {
        Report = report!;
    }
}
