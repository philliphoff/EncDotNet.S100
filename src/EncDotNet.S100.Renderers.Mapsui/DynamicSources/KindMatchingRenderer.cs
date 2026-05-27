using EncDotNet.S100.DynamicSources;
using Mapsui;

namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// Dispatches on <see cref="DynamicFeature.Kind"/> via exact or
/// prefix match. Useful for adapters that publish multiple feature
/// kinds (e.g. AIS cargo vs tanker vs passenger) and want to keep
/// per-kind styling in separate small renderers.
/// </summary>
/// <remarks>
/// <para>
/// Match policy: longest registered key wins. A registration of
/// <c>"vessel"</c> matches any feature with
/// <see cref="DynamicFeature.Kind"/> equal to <c>"vessel"</c> when
/// <paramref name="prefixMatch"/> is <see langword="false"/>, or
/// equal to <c>"vessel"</c> / starting with <c>"vessel."</c> when
/// <paramref name="prefixMatch"/> is <see langword="true"/>. The
/// latter follows the conventional dot-namespaced
/// <see cref="DynamicFeature.Kind"/> shape (e.g. <c>"vessel.cargo"</c>,
/// <c>"vessel.tanker"</c>).
/// </para>
/// <para>
/// Features whose <see cref="DynamicFeature.Kind"/> is
/// <see langword="null"/> never match.
/// </para>
/// </remarks>
public sealed class KindMatchingRenderer : IDynamicFeatureRenderer
{
    private readonly IReadOnlyList<KeyValuePair<string, IDynamicFeatureRenderer>> _orderedByKind;
    private readonly bool _prefixMatch;

    /// <summary>
    /// Creates a kind-matching renderer with the supplied
    /// per-kind renderer map.
    /// </summary>
    /// <param name="byKind">
    /// Map from <see cref="DynamicFeature.Kind"/> (exact or prefix
    /// per <paramref name="prefixMatch"/>) to the renderer that
    /// handles it.
    /// </param>
    /// <param name="prefixMatch">
    /// When <see langword="true"/>, a registered key <c>k</c>
    /// matches features whose <see cref="DynamicFeature.Kind"/> is
    /// <c>k</c> or starts with <c>k + "."</c>. Defaults to exact
    /// match.
    /// </param>
    public KindMatchingRenderer(
        IReadOnlyDictionary<string, IDynamicFeatureRenderer> byKind,
        bool prefixMatch = false)
    {
        ArgumentNullException.ThrowIfNull(byKind);
        // Longest-key-first to make prefix matching deterministic.
        _orderedByKind = byKind
            .OrderByDescending(p => p.Key.Length)
            .ToArray();
        _prefixMatch = prefixMatch;
    }

    /// <inheritdoc />
    public bool CanRender(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return TryMatch(feature, out _);
    }

    /// <inheritdoc />
    public IEnumerable<IFeature> Render(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return TryMatch(feature, out var renderer)
            ? renderer!.Render(feature)
            : Array.Empty<IFeature>();
    }

    private bool TryMatch(DynamicFeature feature, out IDynamicFeatureRenderer? renderer)
    {
        var kind = feature.Kind;
        if (string.IsNullOrEmpty(kind))
        {
            renderer = null;
            return false;
        }

        foreach (var (key, r) in _orderedByKind)
        {
            if (string.Equals(kind, key, StringComparison.Ordinal))
            {
                renderer = r;
                return true;
            }
            if (_prefixMatch
                && kind.Length > key.Length
                && kind[key.Length] == '.'
                && kind.AsSpan(0, key.Length).SequenceEqual(key))
            {
                renderer = r;
                return true;
            }
        }

        renderer = null;
        return false;
    }
}
