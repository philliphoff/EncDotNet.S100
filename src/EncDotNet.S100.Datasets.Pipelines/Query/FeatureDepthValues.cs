using System.ComponentModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

/// <summary>
/// A feature's depth values as numbers (#556), named as <c>sample</c>'s depth
/// result names them, so reading a depth area's range or a wreck's sounding
/// takes no follow-up call or attribute parsing.
/// </summary>
/// <param name="DepthRangeMinimumMeters">An area's minimum depth (DRVAL1, <c>depthRangeMinimumValue</c>), in metres.</param>
/// <param name="DepthRangeMaximumMeters">An area's maximum depth (DRVAL2, <c>depthRangeMaximumValue</c>), in metres.</param>
/// <param name="ValueOfSoundingMeters">A danger's or sounding's depth (VALSOU, <c>valueOfSounding</c>), in metres; negative for a drying height.</param>
/// <param name="WaterLevelEffect">The S-101 <c>waterLevelEffect</c> code (WATLEV): 1 partly submerged at high water, 2 always dry, 3 always under water, 4 covers and uncovers, 5 awash, 6 subject to flooding, 7 floating.</param>
public sealed record FeatureDepthValues(
    [property: Description("An area's minimum depth (DRVAL1, depthRangeMinimumValue) in metres, positive down.")] double? DepthRangeMinimumMeters = null,
    [property: Description("An area's maximum depth (DRVAL2, depthRangeMaximumValue) in metres, positive down.")] double? DepthRangeMaximumMeters = null,
    [property: Description("A danger's or sounding's depth (VALSOU, valueOfSounding) in metres, positive down; negative for a drying height.")] double? ValueOfSoundingMeters = null,
    [property: Description("The waterLevelEffect code (WATLEV): 1 partly submerged at high water, 2 always dry, 3 always under water, 4 covers and uncovers, 5 awash, 6 subject to flooding, 7 floating.")] int? WaterLevelEffect = null)
{
    private static readonly string[] SoundingCodes = ["valueOfSounding", "VALSOU"];
    private static readonly string[] WaterLevelEffectCodes = ["waterLevelEffect", "WATLEV"];

    /// <summary>The feature's depth values, or <c>null</c> when it carries none.</summary>
    /// <param name="feature">The feature.</param>
    /// <returns>The values, or <c>null</c>.</returns>
    public static FeatureDepthValues? From(IS100Feature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        var attributes = feature.Attributes;
        if (attributes.Count == 0)
        {
            return null;
        }

        var minimum = Read(attributes, ChartedDepthResolver.MinimumDepthAttributeCodes);
        var maximum = Read(attributes, ChartedDepthResolver.MaximumDepthAttributeCodes);
        var sounding = Read(attributes, SoundingCodes);
        var effect = Read(attributes, WaterLevelEffectCodes);
        return minimum is null && maximum is null && sounding is null && effect is null
            ? null
            : new FeatureDepthValues(minimum, maximum, sounding, effect is { } e ? (int)e : null);
    }

    private static double? Read(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<string> codes)
    {
        foreach (var code in codes)
        {
            if (attributes.TryGetValue(code, out var raw) && ChartedDepthResolver.TryReadMeters(raw, out var value))
            {
                return value;
            }
        }

        return null;
    }
}
