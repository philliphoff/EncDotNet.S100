using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Cap;

/// <summary>Where a <see cref="CapWarningArea"/>'s outline came from.</summary>
public enum CapShapeSource
{
    /// <summary>The area's own <c>polygon</c>s (and any <c>circle</c>s).</summary>
    Polygon,

    /// <summary>The area's <c>circle</c>s only, each drawn as a 64-vertex polygon.</summary>
    Circle,

    /// <summary>The area's zone codes, resolved against a <see cref="MarineZoneLayer"/>.</summary>
    Zones,

    /// <summary>Nothing to draw: no shape, and no zone code the layer knows.</summary>
    None,
}

/// <summary>
/// One CAP area ready to draw and pick: the alert and info it belongs to, its
/// outline, where that came from, the zone codes that couldn't be resolved,
/// and when it's in force.
/// </summary>
public sealed record CapWarningArea
{
    /// <summary>
    /// The area's feature id within the file, stable for a given file:
    /// <c>alert{n}.info{n}.area{n}</c>, counting from 1 in document order.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The alert.</summary>
    public required CapAlert Alert { get; init; }

    /// <summary>The info block the area belongs to.</summary>
    public required CapInfo Info { get; init; }

    /// <summary>The area as the file gives it.</summary>
    public required CapArea Area { get; init; }

    /// <summary>The outline to draw, WGS-84: one surface per polygon, circle or zone part. Empty for <see cref="CapShapeSource.None"/>.</summary>
    public required IReadOnlyList<SurfacePart> Surfaces { get; init; }

    /// <summary>Where <see cref="Surfaces"/> came from.</summary>
    public required CapShapeSource ShapeSource { get; init; }

    /// <summary>The zones the outline was built from, for <see cref="CapShapeSource.Zones"/>; otherwise empty.</summary>
    public IReadOnlyList<MarineZone> Zones { get; init; } = [];

    /// <summary>
    /// The area's zone codes that the marine zone layer doesn't have, when the
    /// outline had to come from zones. They are listed, not dropped: in the
    /// pick report and in <c>s100 info</c>.
    /// </summary>
    public IReadOnlyList<string> UnmatchedZoneCodes { get; init; } = [];

    /// <summary>
    /// When the warning is in force: from <c>onset</c> (else <c>effective</c>,
    /// else <c>sent</c>) to the later of <c>expires</c> and the NWS hazard end
    /// (<see cref="CapInfo.Ends"/>). Either end may be open.
    /// </summary>
    public required NavigationalWarningValidity Validity { get; init; }

    /// <summary>
    /// When a later message in the same file that updates or cancels this
    /// alert was sent, which ends this one; <see langword="null"/> when none does.
    /// </summary>
    public DateTimeOffset? SupersededAt { get; init; }

    /// <summary>The identifier of the message that superseded it, or <see langword="null"/>.</summary>
    public string? SupersededBy { get; init; }

    /// <summary>
    /// Whether the message is one the map draws at all: an <c>Alert</c> or
    /// <c>Update</c> (not a <c>Cancel</c>, <c>Ack</c> or <c>Error</c>) whose
    /// status isn't <c>Draft</c>.
    /// </summary>
    public bool IsDrawable =>
        Alert.MessageType is CapMessageType.Alert or CapMessageType.Update
        && !string.Equals(Alert.Status, "Draft", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The warning's status at <paramref name="time"/>, by the in-force rule
    /// S-124 navigational warnings use (<see cref="WarningValidity.StatusAt"/>):
    /// <see cref="Validity"/>, ended from <see cref="SupersededAt"/>.
    /// </summary>
    /// <param name="time">The time, usually the view time.</param>
    public NavigationalWarningStatus StatusAt(DateTimeOffset time) =>
        WarningValidity.StatusAt([Validity], SupersededAt, time);

    /// <summary>
    /// Whether the map draws the area at <paramref name="time"/>: it is
    /// <see cref="IsDrawable"/>, has an outline, and is in force then. With no
    /// time (a host with no clock, or a headless render with no time step),
    /// every drawable area not superseded by another message in the file.
    /// </summary>
    /// <param name="time">The view time, or <see langword="null"/>.</param>
    public bool IsDrawnAt(DateTimeOffset? time) =>
        IsDrawable
        && Surfaces.Count > 0
        && (time is { } t ? StatusAt(t) == NavigationalWarningStatus.InForce : SupersededAt is null);
}

/// <summary>
/// Every area of a CAP file, resolved to outlines and timed: what a CAP
/// processor draws and picks.
/// </summary>
public sealed class CapWarningSet
{
    private CapWarningSet(CapDocument document, IReadOnlyList<CapWarningArea> areas)
    {
        Document = document;
        Areas = areas;
        UnmatchedZoneCodes = areas.SelectMany(a => a.UnmatchedZoneCodes).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Changes = areas
            .Where(a => a.IsDrawable)
            .SelectMany(a => WarningValidity.Changes([a.Validity], a.SupersededAt))
            .Select(t => t.UtcDateTime)
            .Distinct()
            .Order()
            .ToArray();
    }

    /// <summary>The file read.</summary>
    public CapDocument Document { get; }

    /// <summary>Every area of every alert, in document order.</summary>
    public IReadOnlyList<CapWarningArea> Areas { get; }

    /// <summary>Every zone code no area could resolve, sorted, each once.</summary>
    public IReadOnlyList<string> UnmatchedZoneCodes { get; }

    /// <summary>
    /// The instants (UTC, ascending, distinct) at which the set of areas in
    /// force can change: each drawable warning's start, the tick after its
    /// end, and when it is superseded.
    /// </summary>
    public IReadOnlyList<DateTime> Changes { get; }

    /// <summary>The areas the map draws at <paramref name="time"/> (see <see cref="CapWarningArea.IsDrawnAt"/>).</summary>
    /// <param name="time">The view time, or <see langword="null"/>.</param>
    public IEnumerable<CapWarningArea> DrawnAt(DateTimeOffset? time) => Areas.Where(a => a.IsDrawnAt(time));

    /// <summary>Resolves every area of <paramref name="document"/>.</summary>
    /// <param name="document">The CAP file read.</param>
    /// <param name="zones">The marine zones to resolve zone codes against; <see cref="MarineZoneLayer.Default"/> when <see langword="null"/>.</param>
    public static CapWarningSet Build(CapDocument document, MarineZoneLayer? zones = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        zones ??= MarineZoneLayer.Default;

        // Updates and cancellations in the same file end what they reference
        // from when they were sent.
        var superseded = new Dictionary<string, (DateTimeOffset Sent, string By)>(StringComparer.Ordinal);
        foreach (var alert in document.Alerts)
        {
            if (alert.MessageType is not (CapMessageType.Update or CapMessageType.Cancel))
                continue;
            foreach (var reference in alert.References)
            {
                if (!superseded.TryGetValue(reference, out var current) || alert.Sent < current.Sent)
                    superseded[reference] = (alert.Sent, alert.Identifier);
            }
        }

        var areas = new List<CapWarningArea>();
        for (var a = 0; a < document.Alerts.Count; a++)
        {
            var alert = document.Alerts[a];
            var ended = superseded.TryGetValue(alert.Identifier, out var by) ? by : ((DateTimeOffset, string)?)null;
            var drawnSignatures = new List<string>();
            for (var i = 0; i < alert.Infos.Count; i++)
            {
                var info = alert.Infos[i];

                // An info that repeats an earlier one's areas in another
                // language isn't drawn twice.
                var signature = Signature(info);
                if (i > 0 && drawnSignatures.Contains(signature))
                    continue;
                drawnSignatures.Add(signature);

                var validity = ValidityOf(alert, info);
                for (var r = 0; r < info.Areas.Count; r++)
                {
                    var area = Resolve(info.Areas[r], zones);
                    areas.Add(area with
                    {
                        Id = $"alert{a + 1}.info{i + 1}.area{r + 1}",
                        Alert = alert,
                        Info = info,
                        Validity = validity,
                        SupersededAt = ended?.Item1,
                        SupersededBy = ended?.Item2,
                    });
                }
            }
        }

        return new CapWarningSet(document, areas);
    }

    /// <summary>
    /// When an info is in force: from <c>onset</c>, else <c>effective</c>, else
    /// the alert's <c>sent</c>; to the later of <c>expires</c> and
    /// <see cref="CapInfo.Ends"/>.
    /// </summary>
    /// <remarks>
    /// The NWS sets <c>expires</c> to when the message should be replaced, which
    /// can be before the hazard starts (a gale warning for tomorrow that expires
    /// this evening); it gives the hazard's end as <c>ends</c> (GeoJSON) or the
    /// <c>eventEndingTime</c> parameter (CAP XML). Using the later of the two
    /// keeps such a warning drawn while its hazard lasts.
    /// </remarks>
    /// <param name="alert">The alert.</param>
    /// <param name="info">One of its info blocks.</param>
    public static NavigationalWarningValidity ValidityOf(CapAlert alert, CapInfo info)
    {
        ArgumentNullException.ThrowIfNull(alert);
        ArgumentNullException.ThrowIfNull(info);
        DateTimeOffset? start = info.Onset ?? info.Effective ?? (alert.Sent == default ? null : alert.Sent);
        DateTimeOffset? end = (info.Expires, info.Ends) switch
        {
            ({ } expires, { } ends) => expires > ends ? expires : ends,
            ({ } expires, null) => expires,
            (null, { } ends) => ends,
            _ => null,
        };
        return new NavigationalWarningValidity(start?.ToUniversalTime(), end?.ToUniversalTime());
    }

    private static string Signature(CapInfo info) =>
        string.Join('|', info.Areas.Select(a =>
            $"{a.Polygons.Count}:{a.Circles.Count}:{string.Join(',', a.ZoneCodes)}:{string.Join(';', a.Polygons.Select(p => p.Count > 0 ? p[0].ToString() : ""))}"));

    private static CapWarningArea Resolve(CapArea area, MarineZoneLayer zones)
    {
        var surfaces = new List<SurfacePart>();
        foreach (var polygon in area.Polygons)
            surfaces.Add(new SurfacePart(polygon, []));
        foreach (var circle in area.Circles)
            surfaces.Add(new SurfacePart(CapGeometry.CircleToPolygon(circle), []));

        if (surfaces.Count > 0)
        {
            return Partial(area, surfaces, area.Polygons.Count > 0 ? CapShapeSource.Polygon : CapShapeSource.Circle, [], []);
        }

        var matched = new List<MarineZone>();
        var unmatched = new List<string>();
        foreach (var code in area.ZoneCodes)
        {
            if (zones.Find(code) is { } zone)
            {
                matched.Add(zone);
                surfaces.AddRange(zone.Surfaces);
            }
            else
            {
                unmatched.Add(code);
            }
        }

        return Partial(area, surfaces, surfaces.Count > 0 ? CapShapeSource.Zones : CapShapeSource.None, matched, unmatched);
    }

    // The resolved part; Build fills in the alert, info, id and timing.
    private static CapWarningArea Partial(
        CapArea area,
        IReadOnlyList<SurfacePart> surfaces,
        CapShapeSource source,
        IReadOnlyList<MarineZone> zones,
        IReadOnlyList<string> unmatched) =>
        new()
        {
            Id = string.Empty,
            Alert = Placeholder,
            Info = PlaceholderInfo,
            Area = area,
            Surfaces = surfaces,
            ShapeSource = source,
            Zones = zones,
            UnmatchedZoneCodes = unmatched,
            Validity = new NavigationalWarningValidity(null, null),
        };

    private static readonly CapAlert Placeholder = new() { Identifier = string.Empty };
    private static readonly CapInfo PlaceholderInfo = new();
}
