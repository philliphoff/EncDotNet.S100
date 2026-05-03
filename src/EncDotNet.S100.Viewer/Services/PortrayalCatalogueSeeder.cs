using System;
using System.Collections.Generic;
using System.IO;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Seeds the application's <see cref="PortrayalCatalogueManager"/> from
/// persisted user settings, command-line overrides, and the bundled
/// fallbacks shipped in <c>EncDotNet.S100.Specifications</c>.
/// </summary>
/// <remarks>
/// Seeding is idempotent and additive: existing entries on the manager are
/// preserved, persisted paths are applied if they exist on disk, CLI
/// portrayal-catalogue overrides take precedence, and any spec without a
/// custom catalogue falls back to the bundled one.
/// </remarks>
internal sealed class PortrayalCatalogueSeeder
{
    private readonly ViewerSettings _settings;
    private readonly PortrayalCatalogueManager _manager;

    public PortrayalCatalogueSeeder(
        ViewerSettings settings,
        PortrayalCatalogueManager manager)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(manager);
        _settings = settings;
        _manager = manager;
    }

    /// <summary>
    /// Applies the seeding routine and returns the transient feature-catalogue
    /// path map collected from <paramref name="options"/>.
    /// </summary>
    /// <returns>
    /// A spec → file-path map for CLI-supplied feature catalogues.
    /// The map is intentionally case-insensitive on the spec key.
    /// </returns>
    public IReadOnlyDictionary<string, string> Seed(ViewerCommandSettings? options)
    {
        // Seed catalogue manager from persisted settings
        foreach (var (spec, path) in _settings.CataloguePaths)
        {
            if (Directory.Exists(path))
            {
                _manager.SetPath(spec, path);
            }
        }

        // Apply CLI portrayal catalogues (transient — not persisted)
        if (options?.PortrayalCatalogues is { } cliPcs)
        {
            foreach (var pcPath in cliPcs)
            {
                if (Directory.Exists(pcPath)
                    && CatalogueSpecDetection.DetectPortrayalCatalogueSpec(pcPath) is { } pcSpec)
                {
                    _manager.SetPath(pcSpec, pcPath);
                }
            }
        }

        // Collect CLI feature catalogues (transient — not persisted)
        var transientFcPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options?.FeatureCatalogues is { } cliFcs)
        {
            foreach (var fcPath in cliFcs)
            {
                if (File.Exists(fcPath)
                    && CatalogueSpecDetection.DetectFeatureCatalogueSpec(fcPath) is { } fcSpec)
                {
                    transientFcPaths[fcSpec] = fcPath;
                }
            }
        }

        // Register bundled portrayal catalogues as fallback for any spec not yet configured
        foreach (var spec in Specifications.Specification.AvailableSpecs)
        {
            if (!_manager.HasCatalogue(spec)
                && Specifications.Specification.HasPortrayalCatalogue(spec))
            {
                _manager.SetSource(spec, Specifications.Specification.CreatePortrayalCatalogueSource(spec));
            }
        }

        return transientFcPaths;
    }
}
