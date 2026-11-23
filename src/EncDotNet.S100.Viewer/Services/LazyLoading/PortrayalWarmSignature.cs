using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Services.LazyLoading;

/// <summary>
/// Identifies everything outside a cell that decides its portrayal-cache key,
/// so the portrayal-cache warmer can tell whether a cell it portrayed earlier
/// is still warm without opening it (#1266).
/// </summary>
/// <remarks>
/// The processors key their cache entries on the cell bytes, the catalogue
/// contents, the code that portrays, the mariner settings and the display
/// mode. The cell bytes are in the ledger's cell key; this covers the rest:
/// the mariner settings and display modes of the presentation, the catalogues
/// in effect (<see cref="CatalogueRegistry.Identity"/>: custom and
/// command-line ones by path, size and modification time, so a session-only
/// catalogue or a Reload after an edit changes it), and the build of the
/// assemblies that portray and bundle the built-in catalogues,
/// including the selected Lua engine's, as the processors' keys do. It
/// may change when the cache key does not (a palette or symbol-size change),
/// which only costs a quick re-check of each cell; it must never stay the same
/// when the cache key changes.
/// </remarks>
internal static class PortrayalWarmSignature
{
    /// <summary>Computes the signature for <paramref name="presentation"/> under <paramref name="settings"/>.</summary>
    /// <param name="presentation">The map's current presentation.</param>
    /// <param name="settings">The viewer settings (for the Lua engine).</param>
    /// <param name="catalogues">The catalogues in effect, <see cref="CatalogueRegistry.Identity"/>.</param>
    /// <returns>A lowercase hex SHA-256.</returns>
    public static string Of(MapPresentationState presentation, ViewerSettings settings, string catalogues)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalogues);

        var sb = new StringBuilder("warm:2|");
        sb.Append(presentation.Mariner).Append('|');
        foreach (var (spec, mode) in presentation.EcdisDisplay.ActiveDisplayModes.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            sb.Append("mode:").Append(spec).Append('=').Append(mode).Append('|');
        sb.Append("catalogues:").Append(catalogues).Append('|');
        foreach (var assembly in new[]
                 {
                     typeof(DatasetPipelineFactory).Assembly,
                     typeof(EncDotNet.S100.Pipelines.Vector.Caching.IPortrayalInstructionCache).Assembly,
                     typeof(Specifications.Specification).Assembly,
                     ViewerLuaEngine.AssemblyOf(settings.ResolveLuaEngine()),
                 })
        {
            sb.Append("asm:").Append(assembly.ManifestModule.ModuleVersionId).Append('|');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}
