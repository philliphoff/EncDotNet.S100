using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A reusable Mapsui overlay layer that paints the on-chart overscale "curtain"
/// (issue #441, S-52 / S-101 <c>AP(OVERSC01)</c> Form A) — a subtle pattern of
/// evenly spaced vertical lines — over the regions of loaded cells that are being
/// displayed beyond their compilation scale. A host adds <see cref="Layer"/> to
/// its <c>Map.Layers</c> once, then calls <see cref="Show(IEnumerable{OverscaleRegion})"/>
/// as the loaded cells or the viewport zoom change; <see cref="Clear"/> empties it.
/// </summary>
/// <remarks>
/// <para>
/// This is step 8's "overscale curtain as an optional Mapsui module": like the
/// pick-highlight and dataset-extent-indicator layers it depends only on Mapsui,
/// not on the session, a catalogue, an application palette, a view model, or
/// Avalonia. The <em>region</em> geometry is computed by
/// <see cref="OverscaleCurtain.ComputeRegions"/> in world (EPSG:3857) coordinates
/// and each region is filled with a shared <see cref="OverscaleCurtainStyle"/>,
/// whose <see cref="OverscaleCurtainRenderer"/> draws world-anchored vertical
/// strokes clipped to the region per frame. The pattern therefore stays crisp at
/// any zoom and on HiDPI surfaces and moves with the chart during panning without
/// any per-frame rebuild here.
/// </para>
/// <para>
/// Because the regions are pan- and rotation-invariant (they depend only on the
/// loaded cells and the viewport resolution), a host recomputes them only when the
/// zoom or the set of loaded cells changes and drives this layer with the result —
/// deciding <em>which</em> cells qualify and honouring any on/off toggle is host
/// policy, so the Viewer keeps that in its own controller.
/// </para>
/// <para>
/// Not thread-safe: build and mutate it on the host's UI thread, like any other
/// Mapsui layer. It is cheap to rebuild, so every update replaces the layer's
/// contents wholesale.
/// </para>
/// </remarks>
public sealed class S100OverscaleCurtainLayer
{
    /// <summary>Default <see cref="Mapsui.Layers.ILayer.Name"/> for the overlay.</summary>
    public const string DefaultLayerName = "S-100 Overscale Curtain";

    private readonly OverscaleCurtainStyle _style;
    private readonly MemoryLayer _layer;

    /// <summary>
    /// Creates the overlay. Add <see cref="Layer"/> to a <c>Map.Layers</c>
    /// collection at the z-order the host wants the curtain to appear (above the
    /// chart slice it annotates).
    /// </summary>
    /// <param name="style">
    /// Appearance (line spacing, width, colour); defaults to a new
    /// <see cref="OverscaleCurtainStyle"/>. The same instance is shared by every
    /// region feature — the renderer only reads it — so one style themes the whole
    /// overlay.
    /// </param>
    /// <param name="name">Layer name; defaults to <see cref="DefaultLayerName"/>.</param>
    public S100OverscaleCurtainLayer(
        OverscaleCurtainStyle? style = null,
        string? name = null)
    {
        _style = style ?? new OverscaleCurtainStyle();
        _layer = new MemoryLayer
        {
            Name = name ?? DefaultLayerName,
            Style = null,
            Features = new List<IFeature>(),
        };
    }

    /// <summary>
    /// The Mapsui layer to add to a <c>Map.Layers</c> collection. Starts empty; its
    /// contents are driven by <see cref="Show(IEnumerable{OverscaleRegion})"/> and
    /// <see cref="Clear"/>.
    /// </summary>
    public ILayer Layer => _layer;

    /// <summary>
    /// Replaces the overlay with one curtain-filled polygon per region. Regions
    /// with no (or empty) geometry are skipped; an empty collection clears the
    /// overlay.
    /// </summary>
    /// <param name="regions">
    /// The overscale-curtain regions to paint, typically from
    /// <see cref="OverscaleCurtain.ComputeRegions"/>.
    /// </param>
    public void Show(IEnumerable<OverscaleRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);

        var features = new List<IFeature>();

        foreach (var region in regions)
        {
            if (region.Region is not { IsEmpty: false } geometry)
                continue;

            var feature = new GeometryFeature(geometry);
            feature.Styles.Add(_style);
            features.Add(feature);
        }

        SetFeatures(features);
    }

    /// <summary>Clears the curtain, leaving the layer attached and empty.</summary>
    public void Clear() => SetFeatures(new List<IFeature>());

    private void SetFeatures(List<IFeature> features)
    {
        _layer.Features = features;
        _layer.DataHasChanged();
    }
}
