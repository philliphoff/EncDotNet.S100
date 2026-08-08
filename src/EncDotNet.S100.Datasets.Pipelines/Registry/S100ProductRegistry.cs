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
    /// Registers (or replaces, by normalized <see cref="S100ProductRegistration.Spec"/>)
    /// a product. The spec is canonicalized on the way in (e.g. <c>"S101"</c> →
    /// <c>"S-101"</c>) so a registration made under a non-canonical identifier is
    /// still resolvable by the canonical spec that <see cref="DatasetPipelineFactory"/>
    /// detection produces.
    /// </summary>
    public void Register(S100ProductRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Spec);
        _bySpec[NormalizeSpec(registration.Spec)] = registration;
    }

    /// <summary>Whether a product is registered for <paramref name="spec"/>.</summary>
    public bool IsRegistered(string spec) => _bySpec.ContainsKey(NormalizeSpec(spec));

    /// <summary>The registered spec keys (canonical where recognized).</summary>
    public IReadOnlyCollection<string> RegisteredSpecs => _bySpec.Keys;

    /// <summary>Looks up the registration for <paramref name="spec"/>.</summary>
    public bool TryResolve(
        string spec,
        [NotNullWhen(true)] out S100ProductRegistration? registration) =>
        _bySpec.TryGetValue(NormalizeSpec(spec), out registration);

    /// <summary>
    /// Resolves the registration for <paramref name="spec"/>, throwing
    /// <see cref="NotSupportedException"/> when none is registered.
    /// </summary>
    public S100ProductRegistration Resolve(string spec) =>
        TryResolve(spec, out var registration)
            ? registration
            : throw new NotSupportedException(
                $"No S-100 product is registered for specification '{spec}'.");

    /// <summary>
    /// Canonicalizes a spec key so registrations and look-ups agree regardless of
    /// the exact identifier form used. Recognized S-100 product identifiers
    /// (e.g. <c>"S101"</c>, <c>"s-101"</c>, <c>"  S-101  "</c>) collapse to their
    /// canonical <c>"S-101"</c> form via
    /// <see cref="DatasetPipelineFactory.MapProductIdentifierToSpec"/>; an
    /// unrecognized identifier (a host's own product) is kept as-is apart from
    /// trimming, and the dictionary's case-insensitive comparer handles casing.
    /// </summary>
    private static string NormalizeSpec(string spec)
    {
        ArgumentException.ThrowIfNullOrEmpty(spec);
        return DatasetPipelineFactory.MapProductIdentifierToSpec(spec) ?? spec.Trim();
    }
}
