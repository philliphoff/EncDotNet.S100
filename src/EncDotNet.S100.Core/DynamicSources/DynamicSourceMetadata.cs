namespace EncDotNet.S100.DynamicSources;

/// <summary>
/// Display metadata and renderer-resolution hints for a dynamic
/// feature source. Carried by every <see cref="IDynamicFeatureSource"/>
/// implementation and consumed by the viewer-side overlay glue to
/// resolve the appropriate <c>IDynamicFeatureRenderer</c> and to
/// populate the Layer Stack panel.
/// </summary>
/// <remarks>
/// See <c>docs/design/dynamic-feature-source.md</c> §4.1 for the
/// authoritative description of how <see cref="RendererKey"/>
/// correlates a source instance with a class-level renderer
/// registration via DI keyed services.
/// </remarks>
public sealed record DynamicSourceMetadata
{
    /// <summary>
    /// Human-readable label for the Layer Stack and the title bar.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Lookup key for the <c>IDynamicFeatureRenderer</c> registered
    /// to draw features from this source. When <see langword="null"/>
    /// or unresolved, the viewer falls back to the default renderer.
    /// </summary>
    /// <remarks>
    /// <see cref="RendererKey"/> is <b>class-level</b> — multiple
    /// source instances of the same kind (e.g. two AIS feeds) share a
    /// single renderer registration. Contrast with
    /// <see cref="IDynamicFeatureSource.Id"/>, which is
    /// instance-unique.
    /// </remarks>
    public string? RendererKey { get; init; }

    /// <summary>
    /// Optional longer description shown in tooltips and settings
    /// panels. Not required.
    /// </summary>
    public string? Description { get; init; }
}
