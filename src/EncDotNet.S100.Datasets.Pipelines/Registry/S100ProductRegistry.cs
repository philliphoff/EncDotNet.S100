using System.Diagnostics.CodeAnalysis;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// A mutable, product-agnostic map of canonical spec string
/// (e.g. <c>"S-101"</c>) to the <see cref="S100ProductRegistration"/> that
/// knows how to construct that product's <see cref="IDatasetProcessor"/>.
/// <see cref="DatasetPipelineFactory"/> resolves against a registry instead of a
/// hard-coded <c>switch</c>, so a host can enable a subset of products —
/// e.g. an S-101-only viewer — or add its own. Use
/// <see cref="S100ProductRegistryExtensions.AddAllS100Products"/> for the
/// batteries-included set.
/// </summary>
public sealed class S100ProductRegistry
{
    private readonly Dictionary<string, S100ProductRegistration> _bySpec =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers (or replaces, by <see cref="S100ProductRegistration.Spec"/>) a
    /// product.
    /// </summary>
    public void Register(S100ProductRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrEmpty(registration.Spec);
        _bySpec[registration.Spec] = registration;
    }

    /// <summary>Whether a product is registered for <paramref name="spec"/>.</summary>
    public bool IsRegistered(string spec) => _bySpec.ContainsKey(spec);

    /// <summary>The canonical spec strings currently registered.</summary>
    public IReadOnlyCollection<string> RegisteredSpecs => _bySpec.Keys;

    /// <summary>Looks up the registration for <paramref name="spec"/>.</summary>
    public bool TryResolve(
        string spec,
        [NotNullWhen(true)] out S100ProductRegistration? registration) =>
        _bySpec.TryGetValue(spec, out registration);

    /// <summary>
    /// Resolves the registration for <paramref name="spec"/>, throwing
    /// <see cref="NotSupportedException"/> when none is registered.
    /// </summary>
    public S100ProductRegistration Resolve(string spec) =>
        TryResolve(spec, out var registration)
            ? registration
            : throw new NotSupportedException(
                $"No S-100 product is registered for specification '{spec}'.");
}
