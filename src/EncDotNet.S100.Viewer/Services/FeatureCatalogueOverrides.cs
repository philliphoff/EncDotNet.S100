using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Tracks the viewer-level overrides for feature catalogue content:
/// transient CLI-supplied paths (highest priority) and persisted user
/// settings (medium priority). The shared
/// <see cref="EncDotNet.S100.Features.FeatureCatalogueManager"/> consults
/// this service through its resolver delegate before falling back to
/// bundled catalogues shipped in <c>EncDotNet.S100.Specifications</c>.
/// </summary>
/// <remarks>
/// <para>
/// The viewer's <see cref="EncDotNet.S100.Features.FeatureCatalogueManager"/>
/// is registered as an application singleton so its parse cache survives
/// across dataset reloads. The resolver delegate, however, must be able
/// to observe new CLI overrides discovered during startup
/// (via <see cref="PortrayalCatalogueSeeder"/>) and updates to persisted
/// settings while the viewer is running. This service is the mutable
/// pivot that lets the singleton manager observe those changes without
/// being rebuilt.
/// </para>
/// <para>
/// The viewer is the only client of this service. The keys it uses are
/// product specification names (e.g. <c>"S-101"</c>), matching the
/// string-based <see cref="EncDotNet.S100.Features.FeatureCatalogueManager"/>
/// resolver overload. See S-100 Part 1 §6 for the product specification
/// identifier conventions.
/// </para>
/// </remarks>
internal sealed class FeatureCatalogueOverrides
{
    private readonly ViewerSettings _settings;
    private readonly ConcurrentDictionary<string, string> _transientPaths =
        new(StringComparer.OrdinalIgnoreCase);

    public FeatureCatalogueOverrides(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <summary>
    /// Replaces the set of transient (CLI-supplied) feature catalogue
    /// paths. The keys are product specification names; values are
    /// absolute file paths to <c>FeatureCatalogue.xml</c> files on disk.
    /// </summary>
    public void SetTransientPaths(IReadOnlyDictionary<string, string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _transientPaths.Clear();
        foreach (var (spec, path) in paths)
        {
            if (string.IsNullOrWhiteSpace(spec) || string.IsNullOrWhiteSpace(path)) continue;
            _transientPaths[spec] = path;
        }
    }

    /// <summary>
    /// Returns a readable stream over the user-supplied feature catalogue
    /// for <paramref name="productSpec"/>, or <c>null</c> if none is
    /// configured. Caller owns the stream.
    /// </summary>
    /// <remarks>
    /// Lookup precedence: transient CLI overrides → persisted user
    /// settings. When neither is configured, the method returns
    /// <c>null</c> so the
    /// <see cref="EncDotNet.S100.Features.FeatureCatalogueManager"/>
    /// falls through to its registered <c>IAssetSource</c> for the
    /// bundled fallback (wired in <c>App.ConfigureServices</c> via
    /// <see cref="Specification.CreateFeatureCatalogueSource(string)"/>),
    /// allowing repeated parses to hit the
    /// <c>CachingAssetSource</c> instead of re-opening a fresh manifest
    /// stream each time.
    /// </remarks>
    public Stream? Open(string productSpec)
    {
        if (string.IsNullOrWhiteSpace(productSpec)) return null;

        if (_transientPaths.TryGetValue(productSpec, out var transient) && File.Exists(transient))
        {
            return File.OpenRead(transient);
        }

        if (_settings.FeatureCataloguePaths.TryGetValue(productSpec, out var persisted)
            && File.Exists(persisted))
        {
            return File.OpenRead(persisted);
        }

        // Bundled fallback is handled by FeatureCatalogueManager via a
        // registered IAssetSource (see App.ConfigureServices). Returning
        // null here is what lets that fallback fire.
        return null;
    }
}
