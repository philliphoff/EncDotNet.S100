using System;
using System.Collections.Generic;
using System.Globalization;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;

namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// Default <see cref="IDynamicSourcePickService"/> implementation.
/// Delegates the geometric hit-test to
/// <see cref="DynamicSourceHitTester"/> and projects each hit into a
/// <see cref="DynamicPickHit"/> with localised attribute rows.
/// </summary>
internal sealed class DynamicSourcePickService : IDynamicSourcePickService
{
    private readonly IDynamicFeatureSourceRegistry _registry;

    public DynamicSourcePickService(IDynamicFeatureSourceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <inheritdoc />
    public IReadOnlyList<DynamicPickHit> Pick(MPoint mapPoint, double resolution)
    {
        ArgumentNullException.ThrowIfNull(mapPoint);

        var sources = _registry.GetVisibleSourceInstances();
        if (sources.Count == 0) return Array.Empty<DynamicPickHit>();

        var raw = DynamicSourceHitTester.HitTest(mapPoint, resolution, sources);
        if (raw.Count == 0) return Array.Empty<DynamicPickHit>();

        var hits = new List<DynamicPickHit>(raw.Count);
        foreach (var hit in raw)
        {
            hits.Add(Project(hit));
        }
        return hits;
    }

    private static DynamicPickHit Project(DynamicHit hit)
    {
        var feature = hit.Feature;
        var (lat, lon) = feature.Coordinates[0];

        return new DynamicPickHit
        {
            SourceId = hit.Source.Id,
            SourceDisplayName = hit.Source.Metadata.DisplayName,
            FeatureId = feature.Id,
            Kind = feature.Kind,
            DisplayLabel = ResolveDisplayLabel(feature),
            LastUpdated = feature.LastUpdated,
            Latitude = lat,
            Longitude = lon,
            Motion = feature.Motion,
            VesselGeometry = feature.VesselGeometry,
            Attributes = BuildAttributeRows(feature),
        };
    }

    /// <summary>
    /// Picks a human-readable label, preferring a vessel name when
    /// the source carries one (AIS static data) and falling back to
    /// the feature id (MMSI for AIS, "ownship" for own-ship).
    /// </summary>
    private static string ResolveDisplayLabel(DynamicFeature feature)
    {
        if (feature.Attributes.TryGetValue("vesselName", out var name)
            && name is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }
        return feature.Id;
    }

    /// <summary>
    /// Flattens motion / geometry / attributes into a single ordered
    /// list of label-value rows. Order: position → motion (COG /
    /// heading / SOG) → vessel geometry → declared attributes
    /// (insertion order). Each row's value is pre-formatted.
    /// </summary>
    private static IReadOnlyList<DynamicPickAttributeRow> BuildAttributeRows(DynamicFeature feature)
    {
        var rows = new List<DynamicPickAttributeRow>(8);
        var (lat, lon) = feature.Coordinates[0];

        rows.Add(new DynamicPickAttributeRow(
            Strings.PickReport_Position,
            string.Format(
                CultureInfo.InvariantCulture,
                Strings.PickReport_PositionFormat,
                lat,
                lon)));

        if (feature.Motion is { } motion)
        {
            if (motion.CourseOverGroundDeg is { } cog)
                rows.Add(new DynamicPickAttributeRow(Strings.PickReport_Cog, FormatDegrees(cog)));
            if (motion.HeadingDeg is { } hdg)
                rows.Add(new DynamicPickAttributeRow(Strings.PickReport_Heading, FormatDegrees(hdg)));
            if (motion.SpeedOverGroundKn is { } sog)
                rows.Add(new DynamicPickAttributeRow(Strings.PickReport_Sog, FormatKnots(sog)));
        }

        if (feature.VesselGeometry is { } geom)
        {
            rows.Add(new DynamicPickAttributeRow(
                Strings.PickReport_Dimensions,
                string.Format(
                    CultureInfo.InvariantCulture,
                    Strings.PickReport_DimensionsFormat,
                    geom.LengthMetres,
                    geom.BeamMetres)));
        }

        foreach (var (key, value) in feature.Attributes)
        {
            // vesselName already drives DisplayLabel — surface it in
            // the row list too so the user sees it explicitly.
            rows.Add(new DynamicPickAttributeRow(
                MapAttributeKeyToLabel(key),
                FormatAttributeValue(value)));
        }

        return rows;
    }

    private static string MapAttributeKeyToLabel(string key) => key switch
    {
        "mmsi" => Strings.PickReport_Mmsi,
        "vesselName" => Strings.PickReport_VesselName,
        "callSign" => Strings.PickReport_CallSign,
        _ => key,
    };

    private static string FormatDegrees(double value) =>
        string.Format(CultureInfo.InvariantCulture, Strings.PickReport_DegreesFormat, value);

    private static string FormatKnots(double value) =>
        string.Format(CultureInfo.InvariantCulture, Strings.PickReport_KnotsFormat, value);

    private static string FormatAttributeValue(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
