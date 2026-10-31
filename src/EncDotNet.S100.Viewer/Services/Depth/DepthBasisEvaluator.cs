using EncDotNet.S100.Collections;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>One loaded dataset, as the depth basis sees it.</summary>
/// <param name="DisplayName">The name the Datasets panel shows.</param>
/// <param name="Spec">The product: "S-101", "S-102", "S-104", ….</param>
/// <param name="Active">The dataset's Active flag.</param>
/// <param name="Footprint">Its footprint in degrees (the same one the Timeline's In map view filter tests), or <see langword="null"/> when not known yet.</param>
/// <param name="VerticalDatumCode">Its vertical datum as an S-100 code, or <see langword="null"/> when not declared.</param>
/// <param name="Start">S-104 only: the first time step (UTC).</param>
/// <param name="End">S-104 only: the last time step (UTC).</param>
/// <param name="WaterLevelKind">S-104 only: grid or station.</param>
/// <param name="Run">S-104 only: the run (issue time, UTC).</param>
internal sealed record DepthBasisDataset(
    string DisplayName,
    string Spec,
    bool Active,
    GeoBounds? Footprint,
    int? VerticalDatumCode,
    DateTime? Start = null,
    DateTime? End = null,
    WaterLevelSourceKind? WaterLevelKind = null,
    DateTime? Run = null);

/// <summary>A snapshot of everything the depth basis depends on.</summary>
/// <param name="Datasets">The loaded datasets.</param>
/// <param name="View">The map view in degrees, or <see langword="null"/> before the first view.</param>
/// <param name="ViewTime">The Timeline's view time.</param>
/// <param name="Adjustment">Chart water level adjustment (#1132).</param>
internal sealed record DepthBasisInputs(
    IReadOnlyList<DepthBasisDataset> Datasets,
    GeoBounds? View,
    DateTimeOffset ViewTime,
    IWaterLevelAdjustmentState Adjustment);

/// <summary>
/// Decides what the depths on the chart are relative to (#1191): every rule
/// in <c>docs/design/depth-basis-indicator.md</c> §2.2, free of Avalonia.
/// </summary>
internal static class DepthBasisEvaluator
{
    /// <summary>The products whose depths the chart draws.</summary>
    internal static readonly IReadOnlySet<string> DepthSpecs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "S-101", "S-401", "S-57", "S-102" };

    /// <summary>The water-level product.</summary>
    internal const string WaterLevelSpec = "S-104";

    /// <summary>
    /// How far a fixed-station S-104 series reaches beyond its stations, the
    /// limit the route check uses (<see cref="RouteCheckSourceOptions.MaxStationDistanceMeters"/>).
    /// </summary>
    internal static readonly double StationReachMeters = new RouteCheckSourceOptions().MaxStationDistanceMeters;

    private const double MetersPerDegreeLatitude = 111_320;

    /// <summary>Evaluates the depth basis.</summary>
    /// <param name="inputs">The snapshot.</param>
    /// <returns>The basis; <see cref="DepthBasis.Hidden"/> when no depth-bearing dataset is loaded and active.</returns>
    public static DepthBasis Evaluate(DepthBasisInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var depthDatasets = inputs.Datasets.Where(d => d.Active && DepthSpecs.Contains(d.Spec)).ToList();
        if (depthDatasets.Count == 0)
            return DepthBasis.Hidden;

        var depthSources = depthDatasets
            .Where(d => MeetsView(d.Footprint, inputs.View))
            .OrderBy(d => SpecOrder(d.Spec))
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(d => new DepthBasisSource(d.DisplayName, d.Spec, d.VerticalDatumCode))
            .ToList();

        var waterLevelSources = inputs.Datasets
            .Where(d => d.Active
                && string.Equals(d.Spec, WaterLevelSpec, StringComparison.OrdinalIgnoreCase)
                && MeetsView(Reach(d), inputs.View))
            .OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(d => new DepthBasisSource(d.DisplayName, d.Spec, d.VerticalDatumCode, d.Start, d.End, d.WaterLevelKind, d.Run))
            .ToList();

        var datum = Agreement(depthSources, waterLevelSources);
        var adjustment = inputs.Adjustment.IsEnabled
            ? inputs.Adjustment.Evaluate(inputs.View, inputs.ViewTime)
            : null;

        if (adjustment is null)
        {
            return new DepthBasis(
                DepthBasisKind.Charted,
                waterLevelSources.Count > 0 ? DepthBasisReason.NotApplied : DepthBasisReason.None,
                null,
                inputs.ViewTime,
                null,
                true,
                datum,
                depthSources,
                waterLevelSources);
        }

        if (adjustment.Status == WaterLevelStatus.Applied)
        {
            return new DepthBasis(
                DepthBasisKind.Adjusted,
                DepthBasisReason.None,
                WaterLevelStatus.Applied,
                inputs.ViewTime,
                adjustment.StepTime,
                adjustment.CoversWholeView,
                adjustment.Datum ?? datum,
                depthSources,
                waterLevelSources);
        }

        return new DepthBasis(
            DepthBasisKind.Charted,
            DepthBasisReason.WaterLevel,
            adjustment.Status,
            inputs.ViewTime,
            null,
            true,
            adjustment.Datum ?? datum,
            depthSources,
            waterLevelSources);
    }

    /// <summary>
    /// Whether the depth sources and water-level sources share a vertical
    /// datum: <see cref="DatumAgreement.Different"/> when any pair differs,
    /// <see cref="DatumAgreement.Same"/> when every pair agrees, otherwise
    /// <see cref="DatumAgreement.Unknown"/>. <see langword="null"/> without both.
    /// </summary>
    internal static DatumAgreement? Agreement(
        IReadOnlyList<DepthBasisSource> depthSources,
        IReadOnlyList<DepthBasisSource> waterLevelSources)
    {
        if (depthSources.Count == 0 || waterLevelSources.Count == 0)
            return null;

        var allSame = true;
        foreach (var depth in depthSources)
        {
            foreach (var water in waterLevelSources)
            {
                switch (VerticalDatumCheck.Compare(depth.VerticalDatumCode, water.VerticalDatumCode))
                {
                    case DatumAgreement.Different:
                        return DatumAgreement.Different;
                    case DatumAgreement.Unknown:
                        allSame = false;
                        break;
                }
            }
        }

        return allSame ? DatumAgreement.Same : DatumAgreement.Unknown;
    }

    /// <summary>
    /// Whether a footprint meets the view. An unknown footprint or view
    /// counts as meeting it, so a dataset isn't left out before it has drawn.
    /// </summary>
    internal static bool MeetsView(GeoBounds? footprint, GeoBounds? view) =>
        footprint is not { } f || view is not { } v || f.Intersects(v);

    /// <summary>The footprint a dataset's water level reaches: a station series reaches <see cref="StationReachMeters"/> beyond its stations.</summary>
    private static GeoBounds? Reach(DepthBasisDataset dataset)
    {
        if (dataset.WaterLevelKind != WaterLevelSourceKind.Station || dataset.Footprint is not { } f)
            return dataset.Footprint;

        var padLat = StationReachMeters / MetersPerDegreeLatitude;
        var maxAbsLat = Math.Min(89, Math.Max(Math.Abs(f.South), Math.Abs(f.North)) + padLat);
        var padLon = Math.Min(180, padLat / Math.Cos(maxAbsLat * Math.PI / 180));
        var west = f.West - padLon;
        var east = f.East + padLon;
        if (f.LongitudeSpan + (2 * padLon) >= 360)
        {
            west = -180;
            east = 180;
        }
        else
        {
            west = GeoBounds.NormalizeLongitude(west);
            east = GeoBounds.NormalizeLongitude(east);
        }

        return new GeoBounds(Math.Max(-90, f.South - padLat), west, Math.Min(90, f.North + padLat), east);
    }

    private static int SpecOrder(string spec) =>
        string.Equals(spec, "S-102", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
}
