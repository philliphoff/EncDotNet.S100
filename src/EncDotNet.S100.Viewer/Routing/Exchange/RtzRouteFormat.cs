using System.Globalization;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Routes.Rtz;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Routing.Exchange;

/// <summary>
/// RTZ route plans (IEC 61174 Annex S, versions 1.0, 1.1 and 1.2) as a route
/// exchange format (#961). An RTZ file has no portrayal of its own, so
/// Import route… reads it directly and the received route opens in Planning
/// mode. RTZ gives a route no ID or edition, so a received RTZ route is
/// known by its file.
/// </summary>
public sealed class RtzRouteFormat : IRouteFormat
{
    /// <inheritdoc />
    public string Name => RouteSourceFormats.Rtz;

    /// <inheritdoc />
    public string Description => Strings.Export_RtzDescription;

    /// <inheritdoc />
    public string Audience => Strings.Export_RtzAudience;

    /// <inheritdoc />
    public IReadOnlyList<string> Versions => RtzVersions.Supported;

    /// <inheritdoc />
    public string FileExtension => ".rtz";

    /// <inheritdoc />
    public IReadOnlyList<string> FilePatterns { get; } = ["*.rtz"];

    /// <inheritdoc />
    public bool OpensAsDataset => false;

    /// <inheritdoc />
    public RouteFormatMatch? Sniff(string head)
    {
        ArgumentNullException.ThrowIfNull(head);
        return RtzVersions.Sniff(head) is { } version ? new RouteFormatMatch(version, RtzVersions.IsSupported(version)) : null;
    }

    /// <inheritdoc />
    public Route ReadReceived(string path)
    {
        RtzRoute rtz;
        try
        {
            rtz = RtzReader.Read(path);
        }
        catch (Exception ex) when (ex is RtzFormatException or IOException or UnauthorizedAccessException)
        {
            throw new RouteImportException(Strings.Import_NotARoute, "not_a_route", ex);
        }
        if (!RtzVersions.IsSupported(rtz.Version))
        {
            throw new RouteImportException(
                string.Format(CultureInfo.CurrentCulture, Strings.Import_UnsupportedVersionFormat, Name, rtz.Version),
                "unsupported_version");
        }
        if (rtz.Waypoints.Count < 2)
            throw new RouteImportException(Strings.Import_TooFewWaypoints, "too_few_waypoints");
        return RouteRtzMapping.ToReceived(rtz, path);
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
            keeps.Add(Strings.Copy_RtzKeepsLegEnvelope);
        RouteCopyLists.AddPassage(keeps, received, Strings.Copy_FieldCrossTrack, RoutePassageField.PortCrossTrack, v => PassageFormatting.Format(v, PassageQuantity.Distance, Pipelines.DepthUnit.Metres));
        RouteCopyLists.AddPassage(keeps, received, Strings.Copy_FieldSpeeds, RoutePassageField.Speed, v => PassageFormatting.Format(v, PassageQuantity.Speed, Pipelines.DepthUnit.Metres));
        RouteCopyLists.AddPassage(keeps, received, Strings.Copy_FieldDraft, RoutePassageField.Draft, v => PassageFormatting.Format(v, PassageQuantity.Depth, Pipelines.DepthUnit.Metres));
        RouteCopyLists.AddPassage(keeps, received, Strings.Copy_FieldUkc, RoutePassageField.RequiredUkc, v => PassageFormatting.Format(v, PassageQuantity.Depth, Pipelines.DepthUnit.Metres));
        if (received.Passage.Departure is { } departure)
            keeps.Add(string.Format(culture, Strings.Copy_KeepsDepartureFormat, departure.UtcDateTime.ToString("g", culture) + " UTC"));
        if (received.Info.Vessel is { } vessel && (vessel.Name ?? vessel.Mmsi ?? vessel.Imo) is { } vesselName)
            keeps.Add(string.Format(culture, Strings.Copy_KeepsVesselFormat, vesselName));
        keeps.Add(Strings.Copy_RtzKeepsInfo);

        if (received.Preserved?.Model is RtzRoute rtz)
        {
            var planning = rtz.GetPlanningSchedule();
            if (planning is not null)
            {
                foreach (var schedule in rtz.Schedules.Where(s => !ReferenceEquals(s, planning)))
                    notKept.Add(string.Format(culture, Strings.Copy_NotKeptScheduleFormat, ScheduleName(schedule), ScheduleName(planning)));
            }
            if (planning is not null && Tables(planning).SelectMany(t => t.Elements).Any(HasScheduleDetails))
                notKept.Add(Strings.Copy_RtzNotKeptScheduleDetails);

            var legs = Enumerable.Range(1, rtz.Waypoints.Count - 1).Select(rtz.GetEffectiveLeg).ToList();
            var trimmed = legs.Count(l => l is { DraughtForward: { } f, DraughtAft: { } a } && f != a);
            if (trimmed > 0)
                notKept.Add(trimmed == 1 ? Strings.Copy_RtzNotKeptTrim : string.Format(culture, Strings.Copy_RtzNotKeptTrimFormat, trimmed));
            var detailed = legs.Count(l => l is not null && (l.Masthead is not null || l.LegReport is not null || l.LegInfo is not null || l.LegNote1 is not null));
            if (detailed > 0)
                notKept.Add(detailed == 1 ? Strings.Copy_RtzNotKeptLegDetails : string.Format(culture, Strings.Copy_RtzNotKeptLegDetailsFormat, detailed));

            var info = rtz.Info;
            if (info.RouteStatus is not null || info.VesselVoyage is not null || info.VesselDisplacement is not null
                || info.VesselCargo is not null || info.VesselGm is not null || info.OptimizationMethod is not null
                || info.VesselMaxRoll is not null || info.VesselMaxWave is not null || info.VesselMaxWind is not null
                || info.VesselSpeedMax is not null || info.VesselServiceMin is not null || info.VesselServiceMax is not null
                || info.RouteChangesHistory is not null)
            {
                notKept.Add(Strings.Copy_RtzNotKeptVesselDetails);
            }

            var extended = ExtendedElementCount(rtz);
            if (extended > 0)
                notKept.Add(string.Format(culture, Strings.Copy_RtzNotKeptExtensionsFormat, extended));
        }

        return new RouteCopyDescription(keeps, notKept);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> CannotHold(Route route, RouteExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(options);
        var fields = new List<string>();
        if (options.IncludeCheckSettings && route.Passage.SquatMeters is not null)
            fields.Add(Strings.Export_RtzFieldSquat);
        if (route.Info.Description is not null)
            fields.Add(Strings.Export_RtzFieldDescription);
        if (route.Info.DeparturePortId is not null || route.Info.ArrivalPortId is not null)
            fields.Add(Strings.Export_RtzFieldPorts);
        if (route.Info.Vessel is { } vessel)
        {
            if (vessel.Callsign is not null || vessel.LengthMeters is not null || vessel.BeamMeters is not null)
                fields.Add(Strings.Export_RtzFieldVessel);
            if ((vessel.Mmsi is not null && RouteRtzMapping.Digits(vessel.Mmsi) is null) || (vessel.Imo is not null && RouteRtzMapping.Digits(vessel.Imo) is null))
                fields.Add(Strings.Export_RtzFieldVesselNumbers);
        }
        if (route.Legs.Any(l => l.StarboardChannelLimitMeters is not null || l.PortChannelLimitMeters is not null))
            fields.Add(Strings.Export_RtzFieldChannelLimits);
        if (route.Legs.Any(l => l.SpeedThroughWaterMinKnots is not null || l.SpeedThroughWaterMaxKnots is not null))
            fields.Add(Strings.Export_RtzFieldWaterSpeeds);
        if (route.Legs.Any(l => l.SafetyMarginMeters is not null))
            fields.Add(Strings.Export_RtzFieldSafetyMargin);
        if (route.Waypoints.Any(w => w.Fixed == true))
            fields.Add(Strings.Export_RtzFieldFixed);
        if (route.Source?.RouteId is not null || route.Origin?.RouteId is not null)
            fields.Add(Strings.Export_RtzFieldIdentity);

        var notes = new List<string>();
        if (fields.Count > 0)
            notes.Add(string.Format(CultureInfo.CurrentCulture, Strings.Export_RtzLeavesOutFormat, string.Join(", ", fields)));
        if (route.Preserved?.Model is S421Route s421
            && (s421.ActionPoints.Count > 0 || s421.Schedules.Count > 1 || s421.ExtraAttributes.Count > 0))
        {
            notes.Add(Strings.Export_RtzNoS421Content);
        }
        if (route.Preserved?.Model is RtzRoute && route.HasStalePreservedSchedules && !route.KeepStalePreservedSchedules)
        {
            notes.Add(route.Preserved.ScheduleCount == 1
                ? Strings.Export_StaleScheduleDropped
                : string.Format(CultureInfo.CurrentCulture, Strings.Export_StaleSchedulesDroppedFormat, route.Preserved.ScheduleCount));
        }
        return notes;
    }

    /// <inheritdoc />
    public void Write(Route route, RouteExchangeIdentity identity, RouteExportOptions options, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(options);
        var version = options.Version ?? RtzVersions.Latest;
        var rtz = RouteRtzMapping.ToRtz(
            route,
            identity,
            options.IncludeCheckSettings,
            route.Preserved?.Model as RtzRoute,
            route.WritesPreservedSchedules,
            version);
        RtzWriter.Write(rtz, stream, version);
    }

    /// <inheritdoc />
    public string SerializePreserved(RoutePreservedContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Model is RtzRoute rtz ? RtzWriter.ToXml(rtz) : string.Empty;
    }

    /// <inheritdoc />
    public RoutePreservedContent? DeserializePreserved(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
            return RouteRtzMapping.Preserve(RtzReader.Read(stream));
        }
        catch (RtzFormatException)
        {
            return null;
        }
    }

    private static bool HasEnvelope(RouteLeg leg)
        => leg.SafetyContourMeters is not null || leg.SafetyDepthMeters is not null
            || leg.SpeedOverGroundMinKnots is not null || leg.SpeedOverGroundMaxKnots is not null
            || leg.Note is not null;

    private static IEnumerable<RtzScheduleTable> Tables(RtzSchedule schedule)
        => new[] { schedule.Manual, schedule.Calculated }.OfType<RtzScheduleTable>();

    // What the copy's own schedule can't carry: everything but the waypoint,
    // times and speed.
    private static bool HasScheduleDetails(RtzScheduleElement e)
        => e.EtdWindowBefore is not null || e.EtdWindowAfter is not null || e.EtaWindowBefore is not null || e.EtaWindowAfter is not null
            || e.Stay is not null || e.SpeedWindow is not null || e.WindSpeed is not null || e.WindDirection is not null
            || e.CurrentSpeed is not null || e.CurrentDirection is not null || e.WindLoss is not null || e.WaveLoss is not null
            || e.TotalLoss is not null || e.Rpm is not null || e.Pitch is not null || e.Fuel is not null
            || e.RelFuelSave is not null || e.AbsFuelSave is not null || e.Note is not null;

    // Elements carrying extensions or attributes the model doesn't name.
    private static int ExtendedElementCount(RtzRoute rtz)
    {
        static bool Extended(IReadOnlyList<System.Xml.Linq.XElement> extensions, IReadOnlyDictionary<System.Xml.Linq.XName, string> extras)
            => extensions.Count > 0 || extras.Count > 0;

        var count = (Extended(rtz.Extensions, rtz.ExtraAttributes) ? 1 : 0)
            + (Extended(rtz.Info.Extensions, rtz.Info.ExtraAttributes) ? 1 : 0)
            + (rtz.WaypointsExtensions.Count > 0 ? 1 : 0)
            + (rtz.SchedulesExtensions.Count > 0 ? 1 : 0);
        foreach (var waypoint in rtz.Waypoints)
        {
            count += Extended(waypoint.Extensions, waypoint.ExtraAttributes) ? 1 : 0;
            count += waypoint.Leg is { } leg && Extended(leg.Extensions, leg.ExtraAttributes) ? 1 : 0;
        }
        foreach (var schedule in rtz.Schedules)
        {
            count += Extended(schedule.Extensions, schedule.ExtraAttributes) ? 1 : 0;
            count += Tables(schedule).SelectMany(t => t.Elements).Count(e => Extended(e.Extensions, e.ExtraAttributes));
        }
        return count;
    }

    private static string ScheduleName(RtzSchedule schedule)
        => schedule.Name ?? string.Format(CultureInfo.CurrentCulture, Strings.Copy_ScheduleUnnamedFormat, schedule.Id);
}
