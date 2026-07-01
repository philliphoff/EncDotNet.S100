using System;
using System.Collections.Generic;
using System.Globalization;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// The single source of truth for the S-98 R-101-102-B depth-feature
/// suppression <em>decision</em>: which S-101 skin-of-the-earth feature types
/// an active S-102 bathymetric surface replaces, and the MSC.232(82) §5.8
/// safety-contour exception that keeps the mariner's safety contour visible.
/// </summary>
/// <remarks>
/// <para>
/// The suppression <em>mechanism</em> differs by renderer (the headless path
/// filters encoding-neutral <c>DrawingInstruction</c>s; the Mapsui path builds
/// its layer from the already-filtered slice), but both consult this policy so
/// the rule can never drift between backends. Cites S-98 Ed.2.0.0 Annex A
/// §8.4.1 + Part B §B-3.1.2 + Annex A §A-6.9.1 NOTE + MSC.232(82) §5.8 + IMO
/// MSC.232(82) Annex 11 §10.5.2.
/// </para>
/// </remarks>
public static class S98SuppressionPolicy
{
    /// <summary>
    /// Depth-value equality tolerance (metres) for the safety-contour
    /// exception. A <c>DepthContour</c> whose VALDCO is within this of the
    /// mariner's safety contour survives suppression.
    /// </summary>
    public const double SafetyContourTolerance = 1e-6;

    /// <summary>
    /// The S-101 feature-type codes an active S-102 dataset suppresses
    /// (S-98 Annex A §8.4.1 "skin-of-the-earth feature replacement"). Ordinal
    /// S-100 Part 5 codes.
    /// </summary>
    // TODO PR-L2-RESYNC: confirm against S-100 Part 16 XSD
    public static readonly IReadOnlySet<string> SuppressedFeatureTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "DepthArea",
            "DepthContour",
        };

    /// <summary>
    /// Decides whether a single feature should be suppressed given its
    /// feature-type code and (for depth contours) its VALDCO depth value.
    /// </summary>
    /// <param name="featureType">
    /// The S-100 Part 5 feature-type code, or <see langword="null"/> for a
    /// feature that does not trace back to a single type (never suppressed).
    /// </param>
    /// <param name="depthContourValue">
    /// The numeric VALDCO depth for a <c>DepthContour</c> feature (any numeric
    /// or numeric-string form), or <see langword="null"/>. Ignored for
    /// non-contour types.
    /// </param>
    /// <param name="safetyContour">
    /// The mariner's safety-contour depth. A depth contour equal to this
    /// (within <see cref="SafetyContourTolerance"/>) is preserved.
    /// </param>
    /// <returns><see langword="true"/> to suppress the feature.</returns>
    public static bool ShouldSuppress(string? featureType, object? depthContourValue, double safetyContour)
    {
        if (featureType is null || !SuppressedFeatureTypes.Contains(featureType))
        {
            return false;
        }

        // Safety-contour exception (MSC.232(82) §5.8). Only depth contours
        // carry a numeric depth; depth areas are suppressed unconditionally.
        if (string.Equals(featureType, "DepthContour", StringComparison.Ordinal)
            && TryReadDepth(depthContourValue, out var depth)
            && Math.Abs(depth - safetyContour) <= SafetyContourTolerance)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Coerces a VALDCO value carried as a boxed numeric or numeric string into
    /// a <see cref="double"/>.
    /// </summary>
    public static bool TryReadDepth(object? raw, out double depth)
    {
        switch (raw)
        {
            case double d:
                depth = d;
                return true;
            case float f:
                depth = f;
                return true;
            case int i:
                depth = i;
                return true;
            case long l:
                depth = l;
                return true;
            case string s when double.TryParse(
                s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                depth = parsed;
                return true;
            default:
                depth = double.NaN;
                return false;
        }
    }
}
