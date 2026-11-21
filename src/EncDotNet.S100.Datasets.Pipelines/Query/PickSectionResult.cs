using System.ComponentModel;
using System.Text.Json;
using EncDotNet.S100.Core;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

/// <summary>
/// One <see cref="PickSection"/> of a feature in an MCP result: pick report
/// content that isn't an attribute row, such as an ice egg code.
/// </summary>
/// <param name="Kind">The section's kind (<c>"ice.egg-code"</c>).</param>
/// <param name="Description">What a section of this kind holds; <c>null</c> for a kind the host hasn't registered.</param>
/// <param name="Rows">The section as attribute rows.</param>
/// <param name="Data">The section's typed content; <c>null</c> for a kind the host hasn't registered.</param>
public sealed record PickSectionResult(
    [property: Description("Section kind, e.g. \"ice.egg-code\" for a WMO / SIGRID-3 ice egg code.")] string Kind,
    [property: Description("What a section of this kind holds; null for a kind the host hasn't registered.")] string? Description,
    [property: Description("The section as attribute rows: code, name, value to show and value as encoded.")] IReadOnlyList<IdentifyAttribute> Rows,
    [property: Description("The section's typed content as JSON; for ice.egg-code, the egg's values by row with their WMO symbols, roles and definitions. Null for a kind the host hasn't registered.")] JsonElement? Data)
{
    /// <summary>Projects <paramref name="section"/> through <paramref name="kinds"/>.</summary>
    internal static PickSectionResult From(PickSection section, PickSectionKindRegistry kinds)
    {
        var kind = kinds.Find(section.Kind);
        return new PickSectionResult(
            section.Kind,
            kind?.Description,
            [.. section.ToRows().Select(IdentifyAttribute.From)],
            kind is not null && kind.SectionType.IsInstanceOfType(section) ? kind.Serialize(section) : null);
    }

    /// <summary>
    /// The sections the pick contributors add to a feature read from the
    /// catalogue, or <see langword="null"/> when they add none.
    /// </summary>
    /// <param name="spec">The feature's product.</param>
    /// <param name="feature">The feature.</param>
    /// <param name="rows">The feature's attribute rows when the caller already has them; built from <paramref name="feature"/> otherwise.</param>
    internal static IReadOnlyList<PickSectionResult>? For(
        SpecRef spec,
        IS100Feature feature,
        IReadOnlyList<PickAttribute>? rows = null)
    {
        rows ??= FeatureInfoBuilder.Build(
            feature.Attributes,
            feature.ComplexAttributes.Select(c =>
                new FeatureInfoBuilder.ComplexAttributeRow(c.Code, c.SubAttributes)),
            decoder: null);
        var sections = PickContributorRegistry.Default.Contribute(new PickContext
        {
            Spec = spec,
            FeatureRef = feature.Id,
            FeatureType = feature.FeatureType,
            Attributes = rows,
        });
        return sections.Count == 0
            ? null
            : [.. sections.Select(s => From(s, PickSectionKindRegistry.Default))];
    }
}
