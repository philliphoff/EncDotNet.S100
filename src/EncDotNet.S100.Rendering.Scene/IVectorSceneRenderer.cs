using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// The named contract a rendering backend implements to draw the
/// backend-agnostic S-100 Part 9 vector intermediate representation
/// (<see cref="VectorScene"/>) onto a backend-specific drawing surface.
/// </summary>
/// <remarks>
/// <para>This is the <i>pluggable rendering-backend</i> seam. Portrayal
/// (<c>IVectorPortrayalSource</c> → <c>VectorSceneBuilder</c>) resolves an
/// S-100 dataset into a fully-resolved <see cref="VectorScene"/> — world
/// coordinates in EPSG:3857 metres, sizes in logical display pixels, colours
/// resolved to <c>RgbaColor</c>, symbols to processed SVG, ops in Part 9 draw
/// order (see the unit contract on <see cref="PaintOp"/>). A backend that
/// implements this interface consumes exactly that IR, so an embedder can plug
/// in a renderer (GPU, server-side raster, PDF, SVG, …) that targets neither
/// SkiaSharp nor Mapsui.</para>
/// <para><b>Surface generality.</b> The surface type is a type parameter
/// because backends draw onto different targets: the headless Skia backend
/// draws to an <c>SKCanvas</c>; an SVG backend writes to a
/// <c>System.IO.TextWriter</c> / <c>System.Xml.XmlWriter</c>. Implementations
/// should project the ops' world coordinates to output pixels with
/// <see cref="WorldToScreen"/> (or an equivalent viewport-derived affine) and
/// realise <i>size</i> values (stroke widths, symbol scale, font size) directly
/// in display pixels — <b>not</b> through the world → screen transform.</para>
/// <para>Not every backend fits a synchronous, pull-style
/// <see cref="Render(TSurface, VectorScene, Viewport)"/> call. The Mapsui
/// backend, for example, binds a scene to a layer and renders on its own
/// per-frame schedule; it is a <i>conforming consumer</i> of the same IR rather
/// than an implementer of this interface. Implement this interface when your
/// backend can draw a scene to a surface on demand.</para>
/// </remarks>
/// <typeparam name="TSurface">The backend-specific drawing surface.</typeparam>
public interface IVectorSceneRenderer<in TSurface>
{
    /// <summary>
    /// Draws the fully-resolved <paramref name="scene"/> onto
    /// <paramref name="surface"/>, projecting world coordinates through the
    /// affine derived from <paramref name="viewport"/>.
    /// </summary>
    /// <param name="surface">The backend-specific drawing surface.</param>
    /// <param name="scene">The resolved paint operations, in Part 9 draw order.</param>
    /// <param name="viewport">The display viewport (geographic bounds + pixel size) to project to.</param>
    void Render(TSurface surface, VectorScene scene, Viewport viewport);
}
