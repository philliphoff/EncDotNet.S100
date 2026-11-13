using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Adds a <see cref="PickSection"/> to a picked feature, from any product or
/// source whose feature it accepts. Contributors run when a feature is
/// described, after the processor's <see cref="IDatasetProcessor.GetFeatureInfo"/>,
/// and never change which features a pick finds or their order.
/// </summary>
/// <remarks>
/// Implementations must be pure functions of the context and safe to call from
/// any thread.
/// </remarks>
public interface IPickContributor
{
    /// <summary>
    /// The section this contributor adds to the feature, or
    /// <see langword="null"/> when the feature isn't one it describes.
    /// </summary>
    /// <param name="context">The picked feature.</param>
    PickSection? Contribute(PickContext context);
}

/// <summary>A picked feature, as an <see cref="IPickContributor"/> sees it.</summary>
public sealed record PickContext
{
    /// <summary>The product or source the feature comes from.</summary>
    public required SpecRef Spec { get; init; }

    /// <summary>The feature's reference within its dataset.</summary>
    public required string FeatureRef { get; init; }

    /// <summary>The feature type code, as encoded.</summary>
    public required string FeatureType { get; init; }

    /// <summary>The feature's attribute rows, with their codes and raw values as encoded.</summary>
    public required IReadOnlyList<PickAttribute> Attributes { get; init; }

    /// <summary>
    /// The processor the feature was picked from; <see langword="null"/> when it
    /// comes from a catalogue snapshot (MCP) rather than a loaded processor.
    /// </summary>
    public IDatasetProcessor? Processor { get; init; }

    /// <summary>
    /// Resolves an enumerated attribute value to its definition through the
    /// dataset's feature catalogue, given the catalogue's attribute code and
    /// the value; <see langword="null"/> when there's no catalogue.
    /// </summary>
    public Func<string, string, string?>? ResolveListedValueDefinition { get; init; }

    /// <summary>
    /// The raw value of the top-level attribute <paramref name="code"/>, or
    /// <see langword="null"/> when the feature doesn't have it or it's blank.
    /// </summary>
    /// <param name="code">The attribute code, compared without regard to case.</param>
    public string? RawValue(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        foreach (var attribute in Attributes)
        {
            if (string.Equals(attribute.Code, code, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(attribute.RawValue))
            {
                return attribute.RawValue;
            }
        }
        return null;
    }
}

/// <summary>
/// A processor that can resolve enumerated attribute values to their
/// definitions through its feature catalogue, for pick contributors.
/// </summary>
public interface IListedValueDefinitions
{
    /// <summary>
    /// The definition of <paramref name="rawValue"/> of the attribute
    /// <paramref name="attributeCode"/>, or <see langword="null"/> when the
    /// catalogue doesn't define it.
    /// </summary>
    /// <param name="attributeCode">The catalogue's attribute code.</param>
    /// <param name="rawValue">The value as encoded.</param>
    string? ResolveListedValueDefinition(string attributeCode, string rawValue);
}
