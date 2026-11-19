using System.Globalization;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Cli.Web;

/// <summary>What a click on the map found (issue #1581, D23): the card's content.</summary>
/// <param name="Latitude">The point picked.</param>
/// <param name="Longitude">The point picked.</param>
/// <param name="Position">The point in degrees and minutes, "50°46.23′N 001°11.88′W".</param>
/// <param name="Coverages">The coverage values at the point (current, water level, depth), shown first.</param>
/// <param name="Features">The features at the point, in pick-report order, with the minor ones last.</param>
/// <param name="Notices">Not-for-navigation notices of products that aren't S-100.</param>
public sealed record PickReport(
    double Latitude,
    double Longitude,
    string Position,
    IReadOnlyList<PickCoverage> Coverages,
    IReadOnlyList<PickFeature> Features,
    IReadOnlyList<string> Notices)
{
    /// <summary>The features shown before "Show N more…".</summary>
    public IReadOnlyList<PickFeature> Major => [.. Features.Where(f => !f.Minor)];

    /// <summary>The meta and area features folded behind "Show N more…".</summary>
    public IReadOnlyList<PickFeature> Minor => [.. Features.Where(f => f.Minor)];

    /// <summary>"15 features", "1 feature", or "Nothing here".</summary>
    public string CountText => Features.Count switch
    {
        0 => "No features",
        1 => "1 feature",
        var n => n.ToString(CultureInfo.InvariantCulture) + " features",
    };
}

/// <summary>A coverage value at the point: "S-111 · Surface current · Forecast", "0.3 kn", 110° T.</summary>
/// <param name="Product">The product tag, "S-111".</param>
/// <param name="Title">What it is, "Surface current · Forecast".</param>
/// <param name="Value">The value with its unit, "0.3 kn".</param>
/// <param name="Direction">Degrees true the value points to (a current's direction); <see langword="null"/> when it has none.</param>
/// <param name="Detail">What qualifies it: the time of the step, the trend, the uncertainty.</param>
public sealed record PickCoverage(string Product, string Title, string Value, double? Direction, string? Detail);

/// <summary>One feature at the point.</summary>
/// <param name="Index">Its place in <see cref="PickReport.Features"/>, which the map's highlight uses.</param>
/// <param name="Title">The catalogue name of its type, "Buoy Lateral".</param>
/// <param name="Identity">One line that tells it apart: its name, light description or depth range.</param>
/// <param name="Product">The product tag, "S-101".</param>
/// <param name="DatasetId">The dataset it's in.</param>
/// <param name="Geometry">point, curve or surface.</param>
/// <param name="Rows">Its attributes with catalogue names and decoded values.</param>
/// <param name="Minor">Whether it's a meta or rarely-useful area type, folded behind "Show N more…".</param>
public sealed record PickFeature(
    int Index,
    string Title,
    string? Identity,
    string Product,
    string DatasetId,
    string Geometry,
    IReadOnlyList<PickRow> Rows,
    bool Minor);

/// <summary>An attribute row: a name and a value, or a complex attribute's name and its rows.</summary>
/// <param name="Name">The catalogue name, "Category of lateral mark".</param>
/// <param name="Value">The decoded value, "Port-hand lateral mark"; empty for a complex attribute.</param>
/// <param name="Children">A complex attribute's rows.</param>
public sealed record PickRow(string Name, string Value, IReadOnlyList<PickRow> Children);

/// <summary>Turns a <see cref="FeatureQueryResult"/> into the card (D23) and the highlight geometry (D24).</summary>
internal static class ServePick
{
    /// <summary>The pick's radius: about this many screen pixels around the click.</summary>
    public const double RadiusPixels = 8;

    /// <summary>The most features the card lists.</summary>
    public const int MaxResults = 60;

    // Each highlight geometry keeps at most this many positions.
    private const int MaxPositions = 4000;

    // Geographic types that rarely matter at a point (D23), besides the
    // catalogue's meta and cartographic types.
    private static readonly HashSet<string> MinorTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CautionArea", "RestrictedArea", "RestrictedAreaNavigational", "RestrictedAreaRegulatory",
        "InformationArea", "SurveyCoverage",
        // S-57 meta classes, as read untranslated.
        "M_COVR", "M_QUAL", "M_NSYS", "M_CSCL", "M_SDAT", "M_VDAT", "M_ACCY", "M_SREL", "M_HOPA", "M_NPUB",
        "CTNARE", "RESARE",
    };

    private static readonly HashSet<string> NameCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "featureName", "name", "OBJNAM", "NOBJNM",
    };

    /// <summary>The card for <paramref name="result"/>; <paramref name="datasets"/> say whether a coverage is a forecast.</summary>
    public static PickReport Report(FeatureQueryResult result, IReadOnlyList<ServedDataset> datasets)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(datasets);

        var features = result.Features.Select(Feature).ToList();
        // The minor ones go last, so the indexes stay in the order shown.
        var ordered = features.Where(f => !f.Minor).Concat(features.Where(f => f.Minor))
            .Select((f, i) => f with { Index = i })
            .ToList();
        return new PickReport(
            result.Latitude,
            result.Longitude,
            Position(result.Latitude, result.Longitude),
            [.. result.Samples.Select(s => Coverage(s, datasets)).OfType<PickCoverage>()],
            ordered,
            result.Notices);
    }

    /// <summary>
    /// The features' geometries as GeoJSON features, in the order of
    /// <see cref="Report"/>'s list, for the map's highlight (D24).
    /// </summary>
    public static JsonArray Geometries(FeatureQueryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var minor = result.Features.Select(f => IsMinor(f)).ToList();
        var ordered = result.Features.Where((_, i) => !minor[i]).Concat(result.Features.Where((_, i) => minor[i]));
        var array = new JsonArray();
        foreach (var match in ordered)
            array.Add((JsonNode?)Geometry(match.Information?.Feature));
        return array;
    }

    /// <summary>"50°46.23′N 001°11.88′W".</summary>
    public static string Position(double latitude, double longitude)
    {
        longitude = ((longitude + 540) % 360 + 360) % 360 - 180;
        return DegreesMinutes(latitude, 'N', 'S', 2) + " " + DegreesMinutes(longitude, 'E', 'W', 3);
    }

    /// <summary>The hash's <c>pick=</c> value, "50.7705,-1.1980".</summary>
    public static string FormatHash(double latitude, double longitude) =>
        latitude.ToString("0.0#####", CultureInfo.InvariantCulture) + "," + longitude.ToString("0.0#####", CultureInfo.InvariantCulture);

    /// <summary>Reads a <c>pick=</c> value; <see langword="null"/> when it isn't a point.</summary>
    public static (double Latitude, double Longitude)? ParseHash(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        var parts = value.Split(',');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            || latitude is < -90 or > 90 || longitude is < -540 or > 540 || !double.IsFinite(latitude) || !double.IsFinite(longitude))
        {
            return null;
        }

        return (latitude, longitude);
    }

    private static string DegreesMinutes(double value, char positive, char negative, int width)
    {
        var hemisphere = value < 0 ? negative : positive;
        var v = Math.Abs(value);
        var degrees = Math.Floor(v);
        var minutes = Math.Round((v - degrees) * 60, 2);
        if (minutes >= 60)
        {
            degrees += 1;
            minutes = 0;
        }

        return degrees.ToString(new string('0', width), CultureInfo.InvariantCulture) + "°"
            + minutes.ToString("00.00", CultureInfo.InvariantCulture) + "′" + hemisphere;
    }

    private static PickFeature Feature(FeatureQueryMatch match)
    {
        var info = match.Information?.Info;
        var attributes = info?.Attributes ?? [];
        var (identity, fromName) = Identity(match.Match.FeatureType, attributes);
        var rows = attributes
            .Where(a => !(fromName && NameCodes.Contains(a.Code)))
            .Select(Row)
            .OfType<PickRow>()
            .ToList();
        return new PickFeature(
            0,
            info?.FeatureTypeName ?? match.Match.FeatureTypeName ?? match.Match.FeatureType,
            identity,
            match.Match.Spec.Name,
            match.Match.DatasetId.Value,
            match.Match.Geometry,
            rows,
            IsMinor(match));
    }

    private static bool IsMinor(FeatureQueryMatch match) =>
        match.Information?.UseType is "meta" or "cartographic" || MinorTypes.Contains(match.Match.FeatureType);

    private static PickRow? Row(PickAttribute attribute)
    {
        if (attribute.Children.Count > 0)
        {
            var children = attribute.Children.Select(Row).OfType<PickRow>().ToList();
            return children.Count > 0 ? new PickRow(attribute.Name ?? attribute.Code, "", children) : null;
        }

        var value = attribute.DisplayText;
        return string.IsNullOrWhiteSpace(value) ? null : new PickRow(attribute.Name ?? attribute.Code, value, []);
    }

    // One line that tells the feature apart (D23): its name, else a light's
    // description, else a depth range or a sounding. True when it's the name.
    internal static (string? Identity, bool FromName) Identity(string featureType, IReadOnlyList<PickAttribute> attributes)
    {
        if (Name(attributes) is { } name)
            return (name, true);
        if (featureType.StartsWith("Light", StringComparison.OrdinalIgnoreCase) || featureType == "LIGHTS")
        {
            if (LightDescription(attributes) is { } light)
                return (light, false);
        }

        var minimum = Raw(attributes, "depthRangeMinimumValue", "DRVAL1");
        var maximum = Raw(attributes, "depthRangeMaximumValue", "DRVAL2");
        if (minimum is not null || maximum is not null)
        {
            return (minimum is not null && maximum is not null
                ? $"{Number(minimum)} – {Number(maximum)} m"
                : minimum is not null ? $"From {Number(minimum)} m" : $"To {Number(maximum!)} m", false);
        }

        if (Raw(attributes, "valueOfSounding", "VALSOU") is { } sounding)
            return (Number(sounding) + " m", false);
        if (Raw(attributes, "valueOfDepthContour", "VALDCO") is { } contour)
            return (Number(contour) + " m", false);
        return (null, false);
    }

    private static string? Name(IReadOnlyList<PickAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (!NameCodes.Contains(attribute.Code))
                continue;
            if (attribute.Children.Count == 0 && !string.IsNullOrWhiteSpace(attribute.RawValue))
                return attribute.RawValue;
            if (attribute.Children.FirstOrDefault(c => string.Equals(c.Code, "name", StringComparison.OrdinalIgnoreCase)) is { } child
                && !string.IsNullOrWhiteSpace(child.RawValue))
            {
                return child.RawValue;
            }
        }

        return null;
    }

    // "Fl(2) R 10s": character, group, colours and period (S-101 rhythmOfLight;
    // S-57 LITCHR, SIGGRP, COLOUR, SIGPER).
    internal static string? LightDescription(IReadOnlyList<PickAttribute> attributes)
    {
        var character = Raw(attributes, "lightCharacteristic", "LITCHR");
        if (character is null || !LightCharacters.TryGetValue(character, out var abbreviation))
            return null;
        var group = Raw(attributes, "signalGroup", "SIGGRP");
        var colours = Raw(attributes, "colour", "COLOUR");
        var period = Raw(attributes, "signalPeriod", "SIGPER");

        var text = abbreviation;
        if (group is { Length: > 0 } g && g is not "()" and not "(1)" and not "1")
            text += g.StartsWith('(') ? g : "(" + g + ")";
        if (colours is not null)
        {
            var letters = string.Concat(colours.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(c => Colours.TryGetValue(c, out var letter) ? letter : ""));
            if (letters.Length > 0)
                text += " " + letters;
        }

        if (period is not null && double.TryParse(period, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            text += " " + seconds.ToString("0.##", CultureInfo.InvariantCulture) + "s";
        return text;
    }

    private static readonly Dictionary<string, string> LightCharacters = new(StringComparer.Ordinal)
    {
        ["1"] = "F",
        ["2"] = "Fl",
        ["3"] = "LFl",
        ["4"] = "Q",
        ["5"] = "VQ",
        ["6"] = "UQ",
        ["7"] = "Iso",
        ["8"] = "Oc",
        ["9"] = "IQ",
        ["10"] = "IVQ",
        ["11"] = "IUQ",
        ["12"] = "Mo",
        ["13"] = "FFl",
        ["14"] = "Fl+LFl",
        ["15"] = "Oc+Fl",
        ["16"] = "FLFl",
        ["17"] = "Al.Oc",
        ["18"] = "Al.LFl",
        ["19"] = "Al.Fl",
        ["20"] = "Al.Gr",
        ["25"] = "Q+LFl",
        ["26"] = "VQ+LFl",
        ["27"] = "UQ+LFl",
        ["28"] = "Al",
        ["29"] = "Al.FFl",
    };

    private static readonly Dictionary<string, string> Colours = new(StringComparer.Ordinal)
    {
        ["1"] = "W",
        ["3"] = "R",
        ["4"] = "G",
        ["5"] = "Bu",
        ["6"] = "Y",
        ["9"] = "Am",
        ["10"] = "Vi",
        ["11"] = "Or",
    };

    // The first value of any of the codes, as encoded, searching complex attributes too.
    private static string? Raw(IReadOnlyList<PickAttribute> attributes, params string[] codes)
    {
        foreach (var attribute in attributes)
        {
            var leaf = attribute.Code.LastIndexOf('.') is var dot and > 0 ? attribute.Code[(dot + 1)..] : attribute.Code;
            if (attribute.Children.Count == 0 && codes.Contains(leaf, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(attribute.RawValue))
                return attribute.RawValue;
            if (attribute.Children.Count > 0 && Raw(attribute.Children, codes) is { } nested)
                return nested;
        }

        return null;
    }

    private static string Number(string raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value.ToString("0.##", CultureInfo.InvariantCulture)
            : raw;

    private static PickCoverage? Coverage(IdentifyCommand.CoverageSample sample, IReadOnlyList<ServedDataset> datasets)
    {
        var c = CultureInfo.InvariantCulture;
        var id = sample.Result.DatasetId.Value;
        var dynamicity = datasets.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileNameWithoutExtension(d.File), id, StringComparison.OrdinalIgnoreCase))?.Dynamicity;
        string Title(string what) => dynamicity is { Length: > 0 } d ? what + " · " + d : what;
        static string At(DateTime time) => "at " + time.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC";
        var predicted = sample.Result.Source is not null ? " · Prediction" : "";

        return sample.Result.Value switch
        {
            SurfaceCurrentSample s => new(sample.Spec, Title("Surface current") + predicted, s.SpeedKnots.ToString("0.0", c) + " kn", s.DirectionDegreesTrue, At(s.SampleTime)),
            SurfaceCurrentStationSample s => new(sample.Spec, Title("Surface current") + predicted, s.SpeedKnots.ToString("0.0", c) + " kn", s.DirectionDegreesTrue, At(s.SampleTime)),
            WaterLevelSample w => new(sample.Spec, Title("Water level") + predicted, w.WaterLevelHeight.ToString("+0.00;-0.00;0.00", c) + " m", null, Trend(w.Trend, w.SampleTime)),
            WaterLevelStationSample w => new(sample.Spec, Title("Water level") + predicted, w.WaterLevelHeight.ToString("+0.00;-0.00;0.00", c) + " m", null, Trend(w.Trend, w.SampleTime)),
            DepthSample d => new(sample.Spec, Title("Depth"), d.DepthMeters.ToString("0.0", c) + " m", null, d.UncertaintyMeters is { } u ? "± " + u.ToString("0.0", c) + " m" : null),
            var value => Fields(sample.Spec, Title(sample.Spec), value.GetFields()),
        };

        static string Trend(string trend, DateTime time) =>
            (string.IsNullOrEmpty(trend) ? "" : trend.ToLowerInvariant() + " · ") + At(time);
    }

    private static PickCoverage? Fields(string product, string title, IReadOnlyList<SampledField> fields)
    {
        var shown = fields
            .Where(f => f.Value is not null || f.Text is not null)
            .Select(f => f.Text ?? f.Value!.Value.ToString("0.##", CultureInfo.InvariantCulture) + (f.Unit is { } unit ? " " + unit : ""))
            .ToList();
        return shown.Count == 0 ? null : new PickCoverage(product, title, shown[0], null, shown.Count > 1 ? string.Join(" · ", shown.Skip(1)) : null);
    }

    // GeoJSON of the feature, lon/lat, thinned to MaxPositions positions.
    private static JsonObject? Geometry(IS100Feature? feature)
    {
        if (feature is null)
            return null;
        var total = feature.Points.Count + feature.Curves.Sum(c => c.Count) + feature.Surfaces.Sum(s => s.ExteriorRing.Count + s.InteriorRings.Sum(r => r.Count));
        var step = Math.Max(1, (int)Math.Ceiling(total / (double)MaxPositions));

        JsonObject geometry;
        if (feature.Surfaces.Count > 0)
        {
            geometry = new JsonObject
            {
                ["type"] = "MultiPolygon",
                ["coordinates"] = new JsonArray([.. feature.Surfaces.Select(s =>
                    (JsonNode)new JsonArray([.. new[] { s.ExteriorRing }.Concat(s.InteriorRings).Where(r => r.Count >= 3).Select(r => (JsonNode)Ring(r, step))]))]),
            };
        }
        else if (feature.Curves.Count > 0)
        {
            geometry = new JsonObject
            {
                ["type"] = "MultiLineString",
                ["coordinates"] = new JsonArray([.. feature.Curves.Where(c => c.Count >= 2).Select(c => (JsonNode)Line(c, step))]),
            };
        }
        else if (feature.Points.Count > 0)
        {
            geometry = new JsonObject
            {
                ["type"] = "MultiPoint",
                ["coordinates"] = new JsonArray([.. Thin(feature.Points, step).Select(p => (JsonNode)Position(p))]),
            };
        }
        else
        {
            return null;
        }

        return new JsonObject { ["type"] = "Feature", ["properties"] = new JsonObject(), ["geometry"] = geometry };
    }

    private static JsonArray Line(IReadOnlyList<GeoPosition> positions, int step) =>
        new([.. Thin(positions, step, keepLast: true).Select(p => (JsonNode)Position(p))]);

    private static JsonArray Ring(IReadOnlyList<GeoPosition> positions, int step)
    {
        var ring = Thin(positions, step, keepLast: true).ToList();
        if (ring[0] != ring[^1])
            ring.Add(ring[0]);
        return new JsonArray([.. ring.Select(p => (JsonNode)Position(p))]);
    }

    private static IEnumerable<GeoPosition> Thin(IReadOnlyList<GeoPosition> positions, int step, bool keepLast = false)
    {
        for (var i = 0; i < positions.Count; i += step)
            yield return positions[i];
        if (keepLast && (positions.Count - 1) % step != 0)
            yield return positions[^1];
    }

    private static JsonArray Position(GeoPosition position) =>
        new(Math.Round(position.Longitude, 7), Math.Round(position.Latitude, 7));
}
