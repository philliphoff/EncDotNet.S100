namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Well-known keys set on Mapsui <c>IFeature</c> instances by dataset
/// processors so that the S-98 interoperability rule engine can
/// filter features without re-running the portrayal pipeline.
/// </summary>
/// <remarks>
/// PR-L2 R-101-102-B implementation uses these tags to suppress
/// S-101 <c>DepthArea</c> and <c>DepthContour</c> features when an
/// S-102 dataset is loaded (S-98 Annex A §8.4.1 + Part B §B-3.1.2).
/// Tags are written by <c>S101DatasetProcessor</c> and read by
/// <c>S98DefaultRules.R_101_102_B_SuppressDepthFeatures</c>. Sibling
/// processors are free to add their own tags following the same
/// dotted-namespace convention.
/// </remarks>
public static class FeatureTagKeys
{
    /// <summary>
    /// Feature-type code (S-100 Part 5 / ISO 19110 — e.g.
    /// <c>"DepthArea"</c>, <c>"DepthContour"</c>) of the originating
    /// dataset feature. Set by S-101 / S-57 processors; nullable for
    /// features that don't trace back to a single feature type.
    /// </summary>
    public const string FeatureType = "S100.FeatureType";

    /// <summary>
    /// Numeric depth value, in metres, of a <c>DepthContour</c>
    /// feature (S-101 attribute <c>VALDCO</c>). Used by R-101-102-B
    /// to honour the MSC.232(82) §5.8 safety-contour exception:
    /// the contour matching the mariner's
    /// <see cref="EncDotNet.S100.Pipelines.MarinerSettings.SafetyContour"/>
    /// must remain visible even when S-102 replaces depth shading.
    /// </summary>
    public const string DepthContourValue = "S100.DepthContourValue";
}
