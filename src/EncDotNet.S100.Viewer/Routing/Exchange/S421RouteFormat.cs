using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Routing.Exchange;

/// <summary>
/// S-421 route plans (IEC 63173-1, edition 1.0) as a route exchange format
/// (#960). An S-421 file also opens as a dataset with its own portrayal, so
/// Import route… loads it as a dataset and the received route follows it.
/// </summary>
public sealed partial class S421RouteFormat : IRouteFormat
{
    private const string NamespacePrefix = "http://www.iho.int/S421/gml/";

    /// <inheritdoc />
    public string Name => RouteSourceFormats.S421;

    /// <inheritdoc />
    public string Description => Strings.Export_S421Description;

    /// <inheritdoc />
    public string Audience => Strings.Export_S421Audience;

    /// <inheritdoc />
    public IReadOnlyList<string> Versions { get; } = ["1.0"];

    /// <inheritdoc />
    public string FileExtension => ".gml";

    /// <inheritdoc />
    public IReadOnlyList<string> FilePatterns { get; } = ["*.gml", "*.xml"];

    /// <inheritdoc />
    public bool OpensAsDataset => true;

    /// <inheritdoc />
    public RouteFormatMatch? Sniff(string head)
    {
        ArgumentNullException.ThrowIfNull(head);
        var match = NamespaceVersion().Match(head);
        if (!match.Success)
            return null;
        var version = match.Groups["version"].Value;
        return new RouteFormatMatch(version, Versions.Contains(version));
    }

    /// <inheritdoc />
    public Route ReadReceived(string path)
    {
        S421Route s421;
        try
        {
            s421 = S421RoutePlan.From(S421Dataset.Open(path), out _).Route;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or System.Xml.XmlException or FormatException)
        {
            throw new RouteImportException(Strings.Import_NotARoute, "not_a_route", ex);
        }
        if (s421.Waypoints.Count < 2)
            throw new RouteImportException(Strings.Import_TooFewWaypoints, "too_few_waypoints");
        return RouteS421Mapping.ToReceived(s421, path);
    }

    /// <inheritdoc />
    public RouteCopyDescription DescribeCopy(Route received)
    {
        ArgumentNullException.ThrowIfNull(received);
        var keeps = new List<string>();
        var notKept = new List<string>();
        var culture = CultureInfo.CurrentCulture;

        keeps.Add(string.Format(culture, Strings.Copy_KeepsWaypointsFormat, received.Waypoints.Count));
        keeps.Add(Strings.Copy_KeepsGeometry);
        if (received.Legs.Any(HasEnvelope))
            keeps.Add(Strings.Copy_KeepsLegEnvelope);
        RouteCopyLists.AddPassage(keeps, received, Strings.Copy_FieldCrossTrack, RoutePassageField.PortCrossTrack, v => PassageFormatting.Format(v, PassageQuantity.Distance, Pipelines.DepthUnit.Metres));
        RouteCopyLists.AddPassage(keeps, received, Strings.Copy_FieldSpeeds, RoutePassageField.Speed, v => PassageFormatting.Format(v, PassageQuantity.Speed, Pipelines.DepthUnit.Metres));
        RouteCopyLists.AddPassage(keeps, received, Strings.Copy_FieldDraft, RoutePassageField.Draft, v => PassageFormatting.Format(v, PassageQuantity.Depth, Pipelines.DepthUnit.Metres));
        RouteCopyLists.AddPassage(keeps, received, Strings.Copy_FieldUkc, RoutePassageField.RequiredUkc, v => PassageFormatting.Format(v, PassageQuantity.Depth, Pipelines.DepthUnit.Metres));
        if (received.Passage.Departure is { } departure)
            keeps.Add(string.Format(culture, Strings.Copy_KeepsDepartureFormat, departure.UtcDateTime.ToString("g", culture) + " UTC"));
        if (received.Info.Vessel is { } vessel && (vessel.Name ?? vessel.Mmsi ?? vessel.Imo) is { } vesselName)
            keeps.Add(string.Format(culture, Strings.Copy_KeepsVesselFormat, vesselName));
        keeps.Add(Strings.Copy_KeepsInfo);

        if (received.Preserved?.Model is S421Route s421)
        {
            if (s421.ActionPoints.Count > 0)
                notKept.Add(s421.ActionPoints.Count == 1
                    ? Strings.Copy_NotKeptActionPoint
                    : string.Format(culture, Strings.Copy_NotKeptActionPointsFormat, s421.ActionPoints.Count));
            var planning = RouteS421Mapping.Planning(s421);
            for (var i = 0; i < s421.Schedules.Count; i++)
            {
                if (i == planning?.Index)
                    continue;
                notKept.Add(string.Format(culture, Strings.Copy_NotKeptScheduleFormat, ScheduleName(s421.Schedules[i], i), ScheduleName(s421.Schedules[planning!.Value.Index], planning.Value.Index)));
            }
            if (planning is { } used && s421.Schedules[used.Index].Variants.Count > 1)
                notKept.Add(string.Format(culture, Strings.Copy_NotKeptVariantsFormat, ScheduleName(s421.Schedules[used.Index], used.Index)));
            if (s421.Info is { } info)
            {
                if (info.DeparturePortId2 is not null || info.ArrivalPortId2 is not null || info.DeparturePortCall is not null || info.ArrivalPortCall is not null)
                    notKept.Add(Strings.Copy_NotKeptPorts);
                if (info.Vessel is { } v && (v.VesselType is not null || v.HeightMeters is not null || v.VoyageId is not null))
                    notKept.Add(Strings.Copy_NotKeptVesselExtras);
                if (info.NextRouteReference is not null)
                    notKept.Add(Strings.Copy_NotKeptNextRoute);
            }
            var extended = (s421.ExtraAttributes.Count > 0 ? 1 : 0)
                + (s421.Info?.ExtraAttributes.Count > 0 ? 1 : 0)
                + s421.Waypoints.Count(w => w.ExtraAttributes.Count > 0)
                + s421.Legs.Count(l => l.ExtraAttributes.Count > 0);
            if (extended > 0)
                notKept.Add(string.Format(culture, Strings.Copy_NotKeptExtensionsFormat, extended));
        }

        return new RouteCopyDescription(keeps, notKept);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> CannotHold(Route route, RouteExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(options);
        var notes = new List<string>();
        if (options.IncludeCheckSettings && route.Passage.SquatMeters is not null)
            notes.Add(Strings.Export_S421NoSquat);
        if (route.Preserved?.Model is EncDotNet.S100.Routes.Rtz.RtzRoute)
            notes.Add(Strings.Export_S421NoRtzContent);
        if (route.Preserved?.Model is S421Route && route.HasStalePreservedSchedules && !route.KeepStalePreservedSchedules)
            notes.Add(route.Preserved!.ScheduleCount == 1
                ? Strings.Export_StaleScheduleDropped
                : string.Format(CultureInfo.CurrentCulture, Strings.Export_StaleSchedulesDroppedFormat, route.Preserved.ScheduleCount));
        return notes;
    }

    /// <inheritdoc />
    public void Write(Route route, RouteExchangeIdentity identity, RouteExportOptions options, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(options);
        var s421 = RouteS421Mapping.ToS421(
            route,
            identity,
            options.IncludeCheckSettings,
            route.Preserved?.Model as S421Route,
            route.WritesPreservedSchedules);
        S421DatasetWriter.Write(s421, stream);
    }

    /// <inheritdoc />
    public string SerializePreserved(RoutePreservedContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Model is S421Route s421 ? Encoding.UTF8.GetString(S421DatasetWriter.ToBytes(s421)) : string.Empty;
    }

    /// <inheritdoc />
    public RoutePreservedContent? DeserializePreserved(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            return RouteS421Mapping.Preserve(S421RoutePlan.From(S421Dataset.Open(stream), out _).Route);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or System.Xml.XmlException or FormatException)
        {
            return null;
        }
    }

    private static bool HasEnvelope(RouteLeg leg)
        => leg.StarboardChannelLimitMeters is not null || leg.PortChannelLimitMeters is not null
            || leg.SafetyContourMeters is not null || leg.SafetyDepthMeters is not null
            || leg.SpeedOverGroundMinKnots is not null || leg.SpeedOverGroundMaxKnots is not null
            || leg.SpeedThroughWaterMinKnots is not null || leg.SpeedThroughWaterMaxKnots is not null
            || leg.Note is not null;

    private static string ScheduleName(S421Schedule schedule, int index)
        => schedule.Name ?? string.Format(CultureInfo.CurrentCulture, Strings.Copy_ScheduleUnnamedFormat, schedule.ScheduleNumber ?? index + 1);

    [GeneratedRegex(@"http://www\.iho\.int/S421/gml/(?:cs\d+/)?(?<version>\d+\.\d+)")]
    private static partial Regex NamespaceVersion();
}
