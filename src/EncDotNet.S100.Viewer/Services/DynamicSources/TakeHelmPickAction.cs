using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// "Take helm" as a pick action (data source design note P4): offered on any
/// picked vessel with an MMSI, such as an AIS target, and carried out by
/// pirate mode following that vessel.
/// </summary>
internal static class TakeHelmPickAction
{
    /// <summary>The action's id.</summary>
    public const string Id = "own-ship.take-helm";

    /// <summary>Registers the action, once.</summary>
    /// <param name="registry">The host's pick actions.</param>
    public static void Register(PickActionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (registry.Actions.Any(a => a.Id == Id))
            return;
        // Own-ship is picked like any vessel now (P5); it can't follow itself.
        registry.Register(new PickAction(
            Id,
            Strings.PickReport_TakeHelm,
            context => context.FeatureType != OwnShip.OwnShipSource.FeatureKind && MmsiOf(context.Sections) is not null));
    }

    /// <summary>The MMSI of the vessel among <paramref name="sections"/>, or <see langword="null"/>.</summary>
    /// <param name="sections">A pick's sections.</param>
    public static uint? MmsiOf(IEnumerable<PickSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        return sections.OfType<VesselSection>()
            .Select(v => uint.TryParse(v.Mmsi, NumberStyles.None, CultureInfo.InvariantCulture, out var mmsi) ? mmsi : (uint?)null)
            .FirstOrDefault(m => m is not null);
    }
}
