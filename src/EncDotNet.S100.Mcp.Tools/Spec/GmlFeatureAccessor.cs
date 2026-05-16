using System.Collections.Generic;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Extracts the GML feature collection from a <see cref="LoadedDataset"/>.
/// Every GML-encoded S-100 spec implemented in this codebase models its
/// features as <see cref="IGmlFeature"/>, so a single accessor — rather
/// than per-spec strategies — can power generic feature-query tools.
/// </summary>
/// <remarks>
/// Returns <c>null</c> for catalog entries that are not GML feature
/// collections (coverage products S-102 / S-104 / S-111 and the
/// ISO 8211-encoded S-101). Callers should treat <c>null</c> as
/// "this spec does not contribute to GML feature queries".
/// </remarks>
public static class GmlFeatureAccessor
{
    /// <summary>
    /// Returns the GML features for the supplied dataset, or <c>null</c>
    /// if the dataset's payload is not a GML feature collection.
    /// </summary>
    public static IEnumerable<IGmlFeature>? GetFeatures(LoadedDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return dataset.Data switch
        {
            S122DatasetData s122 => s122.Model.Features,
            S124DatasetData s124 => s124.Model.Features,
            S125DatasetData s125 => s125.Model.Features,
            S127DatasetData s127 => s127.Model.Features,
            S128DatasetData s128 => s128.Model.Features,
            S129DatasetData s129 => s129.Model.Features,
            S131DatasetData s131 => s131.Model.Features,
            S201DatasetData s201 => s201.Model.Features,
            S411DatasetData s411 => s411.Model.Features,
            S421DatasetData s421 => s421.Model.Features,
            _ => null,
        };
    }
}
