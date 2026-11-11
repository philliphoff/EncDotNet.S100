using System.Globalization;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Water level added to the depth values the S-101 portrayal rules read
/// (S-98 Appendix D-3.1, chart-wide water level adjustment). The rules then
/// shade, contour and label the adjusted depths against the mariner's
/// unchanged thresholds.
/// </summary>
/// <remarks>
/// A feature's depth attributes (<c>depthRangeMinimumValue</c>,
/// <c>depthRangeMaximumValue</c>, <c>valueOfDepthContour</c>,
/// <c>valueOfSounding</c>, <c>surroundingDepth</c>,
/// <c>defaultClearanceDepth</c>) get that feature's water level. Each
/// sounding of a multipoint gets the water level at its own position. Depths
/// are positive down and water level positive up, so both are added.
/// </remarks>
public sealed class S101DepthShift
{
    /// <summary>The attributes that hold a depth.</summary>
    public static IReadOnlySet<string> DepthAttributes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "depthRangeMinimumValue",
        "depthRangeMaximumValue",
        "valueOfDepthContour",
        "valueOfSounding",
        "surroundingDepth",
        "defaultClearanceDepth",
    };

    private readonly IReadOnlyDictionary<uint, double> _features;
    private readonly Func<double, double, double?> _soundings;

    /// <summary>Creates the shift.</summary>
    /// <param name="features">The water level for each feature record id whose depth attributes are shifted.</param>
    /// <param name="soundings">The water level at a sounding's position (latitude, longitude), or <see langword="null"/> to leave it charted.</param>
    public S101DepthShift(IReadOnlyDictionary<uint, double> features, Func<double, double, double?> soundings)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(soundings);
        _features = features;
        _soundings = soundings;
    }

    /// <summary>The water level for a feature's depth attributes, if it has one.</summary>
    /// <param name="featureId">The feature record id.</param>
    /// <returns>The water level in metres, or <see langword="null"/>.</returns>
    public double? ForFeature(uint featureId) => _features.TryGetValue(featureId, out var h) ? h : null;

    /// <summary>The water level for a sounding at a position, if it has one.</summary>
    /// <param name="latitude">Latitude in decimal degrees.</param>
    /// <param name="longitude">Longitude in decimal degrees.</param>
    /// <returns>The water level in metres, or <see langword="null"/>.</returns>
    public double? ForSounding(double latitude, double longitude) => _soundings(latitude, longitude);

    internal static string Shift(string value, double height) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var depth)
            ? Math.Round(depth + height, 2).ToString("R", CultureInfo.InvariantCulture)
            : value;
}
