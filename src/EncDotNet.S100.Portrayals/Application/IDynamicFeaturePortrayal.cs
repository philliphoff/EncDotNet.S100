using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Portrayals.Application;

/// <summary>
/// A code-defined portrayal of the features of an
/// <see cref="IDynamicFeatureSource"/>: S-100 Part 9 drawing instructions,
/// built with an <see cref="InstructionBuilder"/>, plus the palette and
/// symbols they resolve against.
/// </summary>
/// <remarks>
/// <para>
/// A dynamic source's features are drawn by a host-specific renderer (for
/// Mapsui, <c>PortrayedDynamicFeatureRenderer</c> in
/// <c>EncDotNet.S100.Renderers.Mapsui</c>). A portrayal keeps the symbology
/// renderer-neutral, so the same instructions draw on the map, headless, and
/// in tests, by the same rules as a vector product's: symbol and text scale,
/// SCAMIN and rotation.
/// </para>
/// <para>
/// Each instruction's feature reference is the feature's
/// <see cref="DynamicFeature.Id"/>; resolve geometry with
/// <see cref="DynamicFeatureGeometryProvider"/>. Implementations must be pure
/// functions of the feature and safe to call from any thread.
/// </para>
/// </remarks>
public interface IDynamicFeaturePortrayal
{
    /// <summary>The palette the instructions' colour tokens and the symbols' palette classes resolve against.</summary>
    ApplicationPalette Palette { get; }

    /// <summary>Resolves a symbol reference to its S-100 SVG, or <see langword="null"/> for an unknown one.</summary>
    Func<string, string?> SymbolProvider { get; }

    /// <summary>Whether this portrayal draws <paramref name="feature"/>.</summary>
    /// <param name="feature">The feature.</param>
    bool CanPortray(DynamicFeature feature);

    /// <summary>The drawing instructions of <paramref name="feature"/>; empty when it has nothing to draw.</summary>
    /// <param name="feature">The feature.</param>
    IReadOnlyList<DrawingInstruction> Portray(DynamicFeature feature);
}
