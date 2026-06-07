namespace EncDotNet.S100;

/// <summary>
/// A renderable layer: an <see cref="S100Dataset"/> viewed through a portrayal
/// lens (a feature catalogue plus a portrayal catalogue). The layer is the
/// composable unit of rendering — a single layer renders one dataset, and (as the
/// API grows) an ordered list of layers composites a multi-product stack
/// (e.g. an S-101 chart under S-102 bathymetry and S-411 sea ice) into one image.
/// </summary>
public sealed class S100Layer
{
    /// <summary>The dataset to render.</summary>
    public required S100Dataset Dataset { get; init; }

    /// <summary>
    /// The feature catalogue to interpret the dataset with. When <c>null</c>, the
    /// catalogue bundled in <c>EncDotNet.S100.Specifications</c> for the dataset's
    /// product specification is used.
    /// </summary>
    public S100FeatureCatalogue? FeatureCatalogue { get; init; }

    /// <summary>
    /// The portrayal catalogue (symbology) to render the dataset with. When
    /// <c>null</c>, the catalogue bundled in <c>EncDotNet.S100.Specifications</c>
    /// for the dataset's product specification is used.
    /// </summary>
    public S100PortrayalCatalogue? PortrayalCatalogue { get; init; }
}
