using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Maps;

/// <summary>
/// A readable, applicable presentation seam for the presentation-mutating tools
/// (<c>set_palette</c>, <c>set_display_category</c>, <c>set_display_mode</c>,
/// and symbol/text scale).
/// </summary>
/// <remarks>
/// <para>
/// Extends the renderer-neutral <see cref="IMapPresentationController"/> (whose
/// <see cref="IMapPresentationController.SetPresentationAsync"/> applies a whole
/// <see cref="MapPresentationState"/> and re-renders) with a read of the
/// <see cref="Current"/> state. That pairing is what lets each mutating tool be
/// a one-field transform — read <see cref="Current"/>, produce
/// <c>Current.WithPalette(…)</c> (or <c>WithEcdisDisplay(…)</c>, etc.), and
/// apply it — instead of a bespoke per-knob setter.
/// </para>
/// <para>
/// Because <see cref="MapPresentationState"/> is map-wide and immutable, reads
/// and applies compose cleanly: repeated tool calls stitch together by feeding
/// each result into the next transform.
/// </para>
/// </remarks>
public interface IPresentationController : IMapPresentationController
{
    /// <summary>The presentation currently applied to the map.</summary>
    MapPresentationState Current { get; }
}
