using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Mcp.Tools.Spec;

/// <summary>
/// Extracts a queryable feature collection from a <see cref="LoadedDataset"/>
/// as <see cref="IS100Feature"/> instances. Every GML-encoded S-100 spec
/// implemented in this codebase models its features as
/// <see cref="IS100Feature"/> directly; the ISO 8211-encoded S-101 exposes
/// the same shape through its pipeline <see cref="Feature"/> records (which
/// implement <see cref="IS100Feature"/>). A single accessor — rather than
/// per-spec strategies — therefore powers generic feature-query tools.
/// </summary>
/// <remarks>
/// Returns <c>null</c> for catalog entries that carry no queryable vector
/// features (the coverage products S-102 / S-104 / S-111). Callers should
/// treat <c>null</c> as "this spec does not contribute to feature queries".
/// </remarks>
public static class FeatureAccessor
{
    /// <summary>
    /// Returns the queryable features for the supplied dataset, or
    /// <c>null</c> if the dataset's payload is not a vector feature
    /// collection.
    /// </summary>
    /// <remarks>
    /// For S-101 the features are resolved lazily on enumeration via
    /// <see cref="S101VectorSource"/>, which materialises feature geometry
    /// from the dataset's spatial records as pipeline <see cref="Feature"/>
    /// instances that already satisfy <see cref="IS100Feature"/>.
    /// </remarks>
    public static IEnumerable<IS100Feature>? GetFeatures(LoadedDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return dataset.Data switch
        {
            S101DatasetData s101 => S101Features(s101.Dataset),
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

    private static IEnumerable<IS100Feature> S101Features(S101Dataset dataset) =>
        new S101VectorSource(dataset).GetFeatures();
}
