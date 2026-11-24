using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Legends;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// What the Reference tab reads about the chart for an entry (R7, #1650):
/// the features it rings and counts in view, and a display setting's
/// current value.
/// </summary>
internal static class ReferenceTargets
{
    /// <summary>
    /// The features <paramref name="entry"/> describes, as hover to find
    /// rings them: a feature type's by its code and S-57 acronym, a symbol's,
    /// or a family's members'. Null for a line style, area fill, attribute or
    /// display setting, which the display lists don't name per feature.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The target, or <see langword="null"/>.</returns>
    public static ChartReferenceTarget? Of(ReferenceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var spec = entry.Key.Spec.Name;
        return entry switch
        {
            FeatureTypeEntry f => new ChartReferenceTarget(spec, f.Key.ToString(), [], f.Alias is { } alias ? [f.Code, alias] : [f.Code]),
            PortrayalItemEntry { IsFamily: true } family => new ChartReferenceTarget(spec, family.Key.ToString(), family.Members, []),
            PortrayalItemEntry { Key.Kind: ReferenceKind.Symbol } symbol => new ChartReferenceTarget(spec, symbol.Key.ToString(), [symbol.Code], []),
            _ => null,
        };
    }

    /// <summary>
    /// The value the viewer gives a portrayal context parameter now, in
    /// words: "On", "Off", "30 m"; <see langword="null"/> for a parameter the
    /// viewer doesn't set, so the catalogue's default holds.
    /// </summary>
    /// <param name="settings">The viewer settings.</param>
    /// <param name="parameter">The context parameter id, e.g. <c>SimplifiedSymbols</c>.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static string? CurrentValue(ViewerSettingsService settings, string parameter)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(parameter);
        var m = settings.BuildMarinerSettings();
        return parameter switch
        {
            "SafetyContour" => Metres(m.SafetyContour.TotalMetres),
            "SafetyDepth" => Metres(m.SafetyDepth.TotalMetres),
            "ShallowContour" => Metres(m.ShallowContour.TotalMetres),
            "DeepContour" => Metres(m.DeepContour.TotalMetres),
            "FourShades" => OnOff(m.FourShades),
            "ShallowWaterDangers" => OnOff(m.ShallowWaterDangers),
            "PlainBoundaries" => OnOff(m.PlainBoundaries),
            "SimplifiedSymbols" => OnOff(m.SimplifiedSymbols),
            "FullLightLines" => OnOff(m.FullLightLines),
            "RadarOverlay" => OnOff(m.RadarOverlay),
            "IgnoreScaleMinimum" => OnOff(m.IgnoreScaleMinimum),
            "NationalLanguage" => string.IsNullOrWhiteSpace(m.NationalLanguage) ? null : m.NationalLanguage,
            _ => null,
        };
    }

    /// <summary>
    /// The value the viewer gives a portrayal context parameter now, as the
    /// rules compare it: "true", "false", or a depth in metres, "30"; for
    /// Why this symbol (R11, #1680). <see langword="null"/> for a parameter
    /// the viewer doesn't set, so the catalogue's default holds.
    /// </summary>
    /// <param name="mariner">The mariner settings the chart is drawn with.</param>
    /// <param name="parameter">The context parameter id, e.g. <c>SimplifiedSymbols</c>.</param>
    /// <returns>The value, invariant, or <see langword="null"/>.</returns>
    public static string? Value(EncDotNet.S100.Pipelines.MarinerSettings mariner, string parameter)
    {
        ArgumentNullException.ThrowIfNull(mariner);
        ArgumentNullException.ThrowIfNull(parameter);
        var m = mariner;
        return parameter switch
        {
            "SafetyContour" => Number(m.SafetyContour.TotalMetres),
            "SafetyDepth" => Number(m.SafetyDepth.TotalMetres),
            "ShallowContour" => Number(m.ShallowContour.TotalMetres),
            "DeepContour" => Number(m.DeepContour.TotalMetres),
            "FourShades" => Bool(m.FourShades),
            "ShallowWaterDangers" => Bool(m.ShallowWaterDangers),
            "PlainBoundaries" => Bool(m.PlainBoundaries),
            "SimplifiedSymbols" => Bool(m.SimplifiedSymbols),
            "FullLightLines" => Bool(m.FullLightLines),
            "RadarOverlay" => Bool(m.RadarOverlay),
            "IgnoreScaleMinimum" => Bool(m.IgnoreScaleMinimum),
            "NationalLanguage" => string.IsNullOrWhiteSpace(m.NationalLanguage) ? null : m.NationalLanguage,
            _ => null,
        };
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string OnOff(bool value) => value ? Strings.Reference_On : Strings.Reference_Off;

    private static string Metres(double value) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Reference_Metres, value.ToString("0.##", CultureInfo.CurrentCulture));
}
