using EncDotNet.S100.Core;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.Pipelines.Reference;

public static partial class RuleProber
{
    /// <summary>
    /// The content hashes of <paramref name="spec"/>'s bundled feature and
    /// portrayal catalogues, as the catalogue managers compute them: the
    /// key the committed probe results (<see cref="RuleProbeStore.FileName"/>)
    /// must carry to be used.
    /// </summary>
    /// <param name="spec">The product specification, such as <c>S-101</c>.</param>
    /// <param name="cancellationToken">Cancels hashing.</param>
    /// <returns>The hashes, or <see langword="null"/> ones for catalogues that aren't bundled.</returns>
    public static async Task<(string? FeatureCatalogueHash, string? PortrayalCatalogueHash)> BundledHashesAsync(
        string spec, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(spec);
        using var features = new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue);
        using var portrayals = new PortrayalCatalogueManager();
        if (Specification.HasPortrayalCatalogue(spec))
            portrayals.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));

        var specRef = new SpecRef(spec, default);
        var fc = await features.GetCatalogueHashAsync(specRef, cancellationToken).ConfigureAwait(false);
        var pc = await portrayals.GetCatalogueHashAsync(specRef, cancellationToken).ConfigureAwait(false);
        return (fc, pc);
    }

    /// <summary>
    /// Probes <paramref name="spec"/>'s bundled catalogues: what
    /// <c>s100 reference probe</c> writes next to the portrayal catalogue.
    /// </summary>
    /// <param name="spec">The product specification, such as <c>S-101</c>.</param>
    /// <param name="traceEngine">An engine with a line hook (LuaCSharp).</param>
    /// <param name="options">Options; defaults when <see langword="null"/>.</param>
    /// <param name="progress">Reports combinations portrayed.</param>
    /// <param name="cancellationToken">Cancels probing.</param>
    /// <returns>The probe results.</returns>
    /// <exception cref="NotSupportedException">The spec isn't probed.</exception>
    /// <exception cref="InvalidOperationException">The spec's catalogues aren't bundled.</exception>
    public static async Task<RuleProbeSet> ProbeBundledAsync(
        string spec,
        ILuaEngine traceEngine,
        RuleProberOptions? options = null,
        IProgress<RuleProbeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(spec);
        ArgumentNullException.ThrowIfNull(traceEngine);
        if (!Supports(spec))
            throw new NotSupportedException($"Probing {spec}'s rules isn't supported; only S-101's are.");

        var (fcHash, pcHash) = await BundledHashesAsync(spec, cancellationToken).ConfigureAwait(false);
        if (fcHash is null || pcHash is null)
            throw new InvalidOperationException($"{spec} has no bundled feature and portrayal catalogues.");

        FeatureCatalogue features;
        await using (var stream = await Specification.OpenFeatureCatalogueAsync(spec, cancellationToken).ConfigureAwait(false))
        {
            features = FeatureCatalogueReader.Read(stream);
        }

        using var portrayal = await PortrayalCatalogueProvider.OpenAsync(
            Specification.CreatePortrayalCatalogueSource(spec), cancellationToken: cancellationToken).ConfigureAwait(false);
        var index = await ChartReferenceIndex.BuildAsync(new SpecRef(spec, default), features, portrayal, cancellationToken)
            .ConfigureAwait(false);
        return await ProbeAsync(index, features, portrayal, traceEngine, fcHash, pcHash, options, progress, cancellationToken)
            .ConfigureAwait(false);
    }
}
