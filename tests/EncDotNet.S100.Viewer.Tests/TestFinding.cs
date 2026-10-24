using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Builds route findings for list, verdict and map tests, with their text already rendered.</summary>
internal static class TestFinding
{
    /// <summary>A catalogue code, or a made-up one in <paramref name="category"/> for a code the catalogue doesn't have.</summary>
    public static RouteFindingCode Code(string value, RouteFindingCategory category, RouteFindingGroup group) =>
        RouteFindingCodes.TryGet(value, out var code) ? code : new RouteFindingCode(value, category, group);

    public static RouteFinding Make(
        string id,
        string code,
        RouteFindingGroup group,
        RouteFindingCategory category,
        string title,
        string meta,
        int? legIndex,
        double? distanceMeters,
        RouteFindingLocation? location,
        RouteFindingFeature? feature = null) =>
        Make(id, Code(code, category, group), group, category, title, meta, legIndex, distanceMeters, location, feature);

    public static RouteFinding Make(
        string id,
        RouteFindingCode code,
        RouteFindingGroup group,
        RouteFindingCategory category,
        string title,
        string meta,
        int? legIndex,
        double? distanceMeters,
        RouteFindingLocation? location,
        RouteFindingFeature? feature = null) =>
        new RouteFinding(id, code, group)
        {
            LegIndex = legIndex,
            Distance = distanceMeters is { } d ? Length.FromMetres(d) : null,
            Location = location,
            Feature = feature,
        }.WithText(title, meta);

    /// <summary>A copy of a finding with another id, group or location.</summary>
    public static RouteFinding With(RouteFinding finding, string? id = null, RouteFindingGroup? group = null, bool clearLocation = false) =>
        new RouteFinding(id ?? finding.Id, finding.Code, group ?? finding.Group)
        {
            LegIndex = finding.LegIndex,
            Distance = finding.Distance,
            Location = clearLocation ? null : finding.Location,
            Feature = finding.Feature,
            Details = finding.Details,
        }.WithText(finding.Title, finding.Meta);
}
