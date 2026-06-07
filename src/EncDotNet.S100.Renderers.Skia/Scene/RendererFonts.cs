using System.Reflection;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// Resolves the default <see cref="SKTypeface"/> used by the headless Skia
/// renderer for text labels.
/// </summary>
/// <remarks>
/// <para>The renderer normally uses <see cref="SKTypeface.Default"/>, which on
/// Linux is backed by <c>fontconfig</c>. When the self-contained
/// <c>SkiaSharp.NativeAssets.Linux.NoDependencies</c> native library is shipped
/// (issue #23), <c>libSkiaSharp.so</c> does not pull in <c>fontconfig</c>, so on
/// a host without <c>fontconfig</c> and a font package installed,
/// <see cref="SKTypeface.Default"/> resolves to an empty typeface
/// (<see cref="SKTypeface.GlyphCount"/> == 0) and labels would render blank.</para>
/// <para>To guarantee the headless render path produces real text without any
/// system font infrastructure, an Open Sans face is embedded and used as a
/// fallback <em>only</em> when the host default is unusable. Where the host does
/// expose fonts (every desktop, and CI runners with <c>fontconfig</c>) the host
/// default is returned unchanged, so existing visual-regression baselines are
/// unaffected.</para>
/// </remarks>
internal static class RendererFonts
{
    private const string EmbeddedFontResource =
        "EncDotNet.S100.Renderers.Skia.Assets.Fonts.OpenSans-Regular.ttf";

    private static readonly Lazy<SKTypeface> LazyDefault = new(ResolveDefault);

    /// <summary>
    /// The typeface to use for label text: the host default when it can render
    /// glyphs, otherwise the embedded Open Sans fallback.
    /// </summary>
    public static SKTypeface Default => LazyDefault.Value;

    private static SKTypeface ResolveDefault() =>
        Select(SKTypeface.Default, LoadEmbeddedFallback) ?? SKTypeface.CreateDefault();

    /// <summary>
    /// Chooses the label typeface: <paramref name="hostDefault"/> when it can
    /// render glyphs, otherwise the embedded fallback produced by
    /// <paramref name="embeddedFactory"/> (only invoked when the host default is
    /// unusable). Falls back to <paramref name="hostDefault"/> if the factory
    /// yields nothing. Exposed for testing so the selection can be validated
    /// deterministically without depending on the host's font configuration.
    /// </summary>
    internal static SKTypeface? Select(SKTypeface? hostDefault, Func<SKTypeface?> embeddedFactory)
    {
        ArgumentNullException.ThrowIfNull(embeddedFactory);

        if (IsUsable(hostDefault))
            return hostDefault;

        return embeddedFactory() ?? hostDefault;
    }

    /// <summary>
    /// Whether the typeface can actually render text (has glyphs and a family
    /// name). An empty <see cref="SKTypeface.Default"/> on a host without
    /// <c>fontconfig</c> fails this check.
    /// </summary>
    internal static bool IsUsable(SKTypeface? typeface) =>
        typeface is { GlyphCount: > 0 } && !string.IsNullOrEmpty(typeface.FamilyName);

    /// <summary>
    /// Loads the embedded Open Sans fallback face, or <see langword="null"/> if
    /// the resource is missing or cannot be decoded. Exposed for testing so the
    /// fallback can be validated deterministically on hosts that already have a
    /// usable system font (where <see cref="Default"/> would not exercise it).
    /// </summary>
    internal static SKTypeface? LoadEmbeddedFallback()
    {
        try
        {
            using var stream = typeof(RendererFonts).Assembly
                .GetManifestResourceStream(EmbeddedFontResource);
            if (stream is null)
                return null;

            return SKTypeface.FromStream(stream);
        }
        catch
        {
            return null;
        }
    }
}
