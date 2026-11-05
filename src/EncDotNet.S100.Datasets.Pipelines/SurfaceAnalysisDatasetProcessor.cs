using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Weather;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Pipeline processor for the WPC coded surface bulletins (design note
/// <c>docs/design/vector-weather.md</c> §8.3): the coded surface analysis
/// (<c>CODSUS</c>, standard or high resolution) and the coded forecast
/// bulletins, read by <see cref="CodedSurfaceBulletinReader"/> into
/// <see cref="SurfaceAnalysis"/> models. Not an S-100 product and not for
/// navigation.
/// </summary>
/// <remarks>
/// <para>
/// This is the data side of #1233 (slice S6): the bulletin, its fronts and
/// pressure centres as features (<see cref="EnumerateFeatures"/>,
/// <see cref="GetFeatureInfoAt"/>), and metadata whose extent is WPC's domain
/// (<see cref="CodedSurfaceBulletin.WpcDomain"/>, V36) and whose time
/// coverage is the bulletin's valid times. Drawing the fronts comes with the
/// portrayal slice; until then the processor draws nothing.
/// </para>
/// <para>
/// The file is read whole at construction, which fails with a
/// <see cref="CodedSurfaceBulletinFormatException"/> naming the file when it
/// can't be decoded.
/// </para>
/// </remarks>
public sealed class SurfaceAnalysisDatasetProcessor : IDatasetProcessor
{
    /// <summary>The spec name the coded surface bulletins are registered under.</summary>
    public const string SpecName = Core.SpecName.Codsus;

    /// <summary>The feature type of every front and trough.</summary>
    public const string FrontFeatureType = "SurfaceFront";

    /// <summary>The feature type of every high and low.</summary>
    public const string PressureCentreFeatureType = "PressureCentre";

    /// <summary>The first line of every pick report on a front or pressure centre.</summary>
    public const string NotForNavigationNotice = "Weather analysis, not an S-100 product and not for navigation.";

    /// <summary>The spec every coded surface bulletin reports: <c>CODSUS</c>, with no edition (the format has none).</summary>
    public static SpecRef CodsusSpec { get; } = new(SpecName, default);

    private readonly Feature[] _features;
    private readonly DatasetMetadata _metadata;

    /// <summary>
    /// Reads the bulletin at <paramref name="path"/>. A bulletin without an
    /// issuance line is dated from the file's last-write time.
    /// </summary>
    /// <param name="path">The file's path.</param>
    public SurfaceAnalysisDatasetProcessor(string path)
        : this(ReadFile(path), Path.GetFileName(path))
    {
    }

    /// <summary>
    /// Reads the bulletin <paramref name="relativePath"/> from
    /// <paramref name="source"/>. A bulletin without an issuance line can't be
    /// dated from an asset source, so it must have one.
    /// </summary>
    /// <param name="source">The asset source holding the file.</param>
    /// <param name="relativePath">The file's source-relative path.</param>
    public SurfaceAnalysisDatasetProcessor(IAssetSource source, string relativePath)
        : this(ReadSource(source, relativePath), AssetSourceHelpers.GetFileName(relativePath))
    {
    }

    /// <summary>Wraps a bulletin already read.</summary>
    /// <param name="bulletin">The bulletin.</param>
    /// <param name="fileName">The file's name, the dataset's id.</param>
    public SurfaceAnalysisDatasetProcessor(CodedSurfaceBulletin bulletin, string fileName)
    {
        ArgumentNullException.ThrowIfNull(bulletin);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Bulletin = bulletin;
        FileName = fileName;
        _features = BuildFeatures(bulletin);
        _metadata = BuildMetadata(bulletin);
    }

    /// <inheritdoc />
    public SpecRef Spec => CodsusSpec;

    /// <summary>The dataset's file name.</summary>
    public string FileName { get; }

    /// <summary>The bulletin, with its analyses.</summary>
    public CodedSurfaceBulletin Bulletin { get; }

    /// <inheritdoc />
    /// <remarks>WPC's domain as the extent (V36), WGS-84, and the first to the last valid time.</remarks>
    public DatasetMetadata Metadata => _metadata;

    /// <summary>Reads the metadata of the bulletin at <paramref name="path"/>: its spec, WPC's domain and its valid times.</summary>
    /// <param name="path">The file's path.</param>
    public static DatasetMetadata ReadMetadata(string path) => BuildMetadata(ReadFile(path));

    /// <summary>Builds the dataset metadata of a bulletin.</summary>
    /// <param name="bulletin">The bulletin.</param>
    public static DatasetMetadata BuildMetadata(CodedSurfaceBulletin bulletin)
    {
        ArgumentNullException.ThrowIfNull(bulletin);
        var times = bulletin.Analyses.Select(a => a.ValidTime.UtcDateTime).ToArray();
        return new DatasetMetadata
        {
            Spec = CodsusSpec,
            Extent = bulletin.Domain,
            HorizontalCrsEpsg = 4326,
            TimeCoverage = times.Length == 0 ? null : new TimeCoverage(times.Min(), times.Max()),
        };
    }

    /// <inheritdoc />
    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        ArgumentNullException.ThrowIfNull(featureRef);
        var index = Array.FindIndex(_features, f => string.Equals(f.Id, featureRef, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : GetFeatureInfoAt(index);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The attributes, in order: the notice, the valid time, the forecast lead
    /// time for a forecast, then for a front its code, intensity and points (as
    /// the bulletin gives them, in order), or for a pressure centre its
    /// pressure and position.
    /// </remarks>
    public FeatureInfo? GetFeatureInfoAt(int ordinal)
    {
        if (ordinal < 0 || ordinal >= _features.Length)
            return null;
        var feature = _features[ordinal];
        var attributes = new List<PickAttribute>
        {
            Attribute("notice", "Notice", NotForNavigationNotice),
            new()
            {
                Code = "validTime",
                Name = "Valid",
                RawValue = feature.Analysis.ValidTime.ToString("yyyy-MM-dd'T'HH:mm'Z'", CultureInfo.InvariantCulture),
                DateTimeValue = feature.Analysis.ValidTime.UtcDateTime,
            },
        };
        if (feature.Analysis.ForecastHours is { } hours)
            attributes.Add(Attribute("forecastHours", "Forecast", string.Create(CultureInfo.InvariantCulture, $"{hours} h")));

        if (feature.Front is { } front)
        {
            attributes.Add(Attribute("code", "Code", front.Code));
            if (front.Strength != SurfaceFrontStrength.Unspecified)
                attributes.Add(Attribute("strength", "Strength", front.Strength.ToString()));
            attributes.Add(Attribute("points", "Points", string.Join(", ", front.Points.Select(FormatPosition))));
        }
        else if (feature.Centre is { } centre)
        {
            attributes.Add(Attribute("pressure", "Pressure", string.Create(CultureInfo.InvariantCulture, $"{centre.PressureHectopascals} hPa")));
            attributes.Add(Attribute("position", "Position", FormatPosition(centre.Position)));
        }

        return new FeatureInfo
        {
            FeatureRef = feature.Id,
            FeatureType = feature.Type,
            FeatureTypeName = feature.Name,
            Attributes = attributes,
        };
    }

    /// <inheritdoc />
    public IEnumerable<FeatureSummary> EnumerateFeatures()
    {
        for (var i = 0; i < _features.Length; i++)
        {
            yield return new FeatureSummary
            {
                FeatureRef = _features[i].Id,
                Ordinal = i,
                FeatureType = _features[i].Type,
                FeatureTypeName = _features[i].Name,
            };
        }
    }

    /// <summary>A front kind's name, for example "Cold front" or "Trough".</summary>
    /// <param name="type">The kind of front.</param>
    public static string NameOf(SurfaceFrontType type) => type switch
    {
        SurfaceFrontType.Cold => "Cold front",
        SurfaceFrontType.Warm => "Warm front",
        SurfaceFrontType.Occluded => "Occluded front",
        SurfaceFrontType.Stationary => "Stationary front",
        SurfaceFrontType.Trough => "Trough",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static CodedSurfaceBulletin ReadFile(string path)
    {
        try
        {
            return CodedSurfaceBulletinReader.Read(path);
        }
        catch (CodedSurfaceBulletinFormatException ex)
        {
            throw new CodedSurfaceBulletinFormatException($"{Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    private static CodedSurfaceBulletin ReadSource(IAssetSource source, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var stream = AssetSourceHelpers.OpenSeekable(source, relativePath);
        try
        {
            return CodedSurfaceBulletinReader.Read(stream, referenceTime: null);
        }
        catch (CodedSurfaceBulletinFormatException ex)
        {
            throw new CodedSurfaceBulletinFormatException($"{AssetSourceHelpers.GetFileName(relativePath)}: {ex.Message}", ex);
        }
    }

    private static Feature[] BuildFeatures(CodedSurfaceBulletin bulletin)
    {
        var features = new List<Feature>();
        for (int a = 0; a < bulletin.Analyses.Count; a++)
        {
            var analysis = bulletin.Analyses[a];
            for (int i = 0; i < analysis.Fronts.Count; i++)
            {
                var front = analysis.Fronts[i];
                features.Add(new Feature($"a{a}.front{i}", FrontFeatureType, NameOf(front.Type), analysis, front, null));
            }

            for (int i = 0; i < analysis.PressureCentres.Count; i++)
            {
                var centre = analysis.PressureCentres[i];
                var name = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{(centre.Kind == PressureCentreKind.High ? "High" : "Low")} {centre.PressureHectopascals} hPa");
                features.Add(new Feature($"a{a}.centre{i}", PressureCentreFeatureType, name, analysis, null, centre));
            }
        }

        return features.ToArray();
    }

    private static PickAttribute Attribute(string code, string name, string value) =>
        new() { Code = code, Name = name, RawValue = value };

    private static string FormatPosition(DataModel.GeoPosition position) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Math.Abs(position.Latitude):0.0}°{(position.Latitude < 0 ? 'S' : 'N')} {Math.Abs(position.Longitude):0.0}°{(position.Longitude < 0 ? 'W' : 'E')}");

    private sealed record Feature(
        string Id,
        string Type,
        string Name,
        SurfaceAnalysis Analysis,
        SurfaceFront? Front,
        PressureCentre? Centre);
}
