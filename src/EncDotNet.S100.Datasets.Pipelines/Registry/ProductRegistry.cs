using System.Diagnostics.CodeAnalysis;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// A mutable, product-agnostic map of canonical spec string
/// (e.g. <c>"S-101"</c>, <c>"GRIB2"</c>) to the <see cref="ProductRegistration"/>
/// that knows how to recognise, construct and describe that product.
/// <see cref="DatasetPipelineFactory"/> resolves against a registry instead of a
/// hard-coded <c>switch</c>, and hosts read product traits
/// (<see cref="ProductRegistration.IsS100"/>,
/// <see cref="ProductRegistration.Portrayal"/>, the default S-98 planes…) from
/// it rather than switching on spec names, so a host can enable a subset of
/// products — e.g. an S-101-only viewer — or add its own without changing.
/// Use <see cref="ProductRegistryExtensions.AddAllS100Products"/> for the
/// batteries-included set.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Default"/> is the process-wide registry the static conveniences
/// (<see cref="DatasetPipelineFactory.DetectProductSpec(string)"/>, a factory
/// built without a registry, <see cref="Catalog.LoadedDatasetProjector"/>, the
/// viewer and the MCP tools) consult. A host that adds a product registers it
/// there once at start-up.
/// </para>
/// <para>
/// The registry is safe to read and register on from several threads.
/// </para>
/// </remarks>
public sealed class ProductRegistry : IProductPlaneTable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ProductRegistration> _bySpec =
        new(StringComparer.OrdinalIgnoreCase);
    private ProductRegistration[] _snapshot = [];

    /// <summary>Creates an empty registry.</summary>
    public ProductRegistry()
    {
        DisplayPlanes = new DefaultDisplayPlaneAuthority(this);
    }

    /// <summary>
    /// The process-wide registry, pre-loaded with every built-in product
    /// (<see cref="S100Products.All"/>). Hosts that don't take a registry of
    /// their own read this one, so a product registered here is detected,
    /// loaded and described everywhere.
    /// </summary>
    public static ProductRegistry Default { get; } = S100Products.CreateDefaultRegistry();

    /// <summary>
    /// The default-plane authority over this registry's
    /// <see cref="ProductRegistration.DefaultPlanes"/>: an unregistered product,
    /// or a layer kind the product gives no plane, lands on
    /// <see cref="S98DisplayPlane.OtherChartOverlays"/>.
    /// </summary>
    public IDisplayPlaneAuthority DisplayPlanes { get; }

    /// <summary>
    /// Registers (or replaces, by normalized <see cref="ProductRegistration.Spec"/>)
    /// a product. The spec is canonicalized on the way in (e.g. <c>"S101"</c> →
    /// <c>"S-101"</c>) so a registration made under a non-canonical identifier is
    /// still resolvable by the canonical spec that <see cref="DatasetPipelineFactory"/>
    /// detection produces.
    /// </summary>
    public void Register(ProductRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Spec);
        var key = NormalizeSpec(registration.Spec);
        lock (_gate)
        {
            _bySpec[key] = registration;
            _snapshot = [.. _bySpec.Values];
        }
    }

    /// <summary>
    /// Removes the registration for <paramref name="spec"/>, returning whether
    /// one was registered.
    /// </summary>
    public bool Unregister(string spec)
    {
        var key = NormalizeSpec(spec);
        lock (_gate)
        {
            if (!_bySpec.Remove(key))
                return false;
            _snapshot = [.. _bySpec.Values];
            return true;
        }
    }

    /// <summary>Whether a product is registered for <paramref name="spec"/>.</summary>
    public bool IsRegistered(string spec) => TryResolve(spec, out _);

    /// <summary>The registered spec keys (canonical where recognized).</summary>
    public IReadOnlyCollection<string> RegisteredSpecs
    {
        get
        {
            lock (_gate)
                return [.. _bySpec.Keys];
        }
    }

    /// <summary>
    /// The registered product registrations, in registration order. Used by
    /// <see cref="DatasetPipelineFactory"/> to run each product's own content
    /// recognizers (<see cref="ProductRegistration.MatchGml"/> and its siblings)
    /// rather than a central per-product switch. A snapshot: registering while
    /// enumerating is safe.
    /// </summary>
    public IReadOnlyCollection<ProductRegistration> Registrations => Volatile.Read(ref _snapshot);

    /// <summary>Looks up the registration for <paramref name="spec"/>.</summary>
    public bool TryResolve(
        string spec,
        [NotNullWhen(true)] out ProductRegistration? registration)
    {
        var key = NormalizeSpec(spec);
        lock (_gate)
            return _bySpec.TryGetValue(key, out registration);
    }

    /// <summary>
    /// Resolves the registration for <paramref name="spec"/>, throwing
    /// <see cref="NotSupportedException"/> when none is registered.
    /// </summary>
    public ProductRegistration Resolve(string spec) =>
        TryResolve(spec, out var registration)
            ? registration
            : throw new NotSupportedException(
                $"No product is registered for specification '{spec}'.");

    /// <summary>
    /// Whether <paramref name="spec"/> is an S-100 product
    /// (<see cref="ProductRegistration.IsS100"/>). An unregistered or blank spec
    /// counts as S-100 unless it is one of the non-S-100 format names
    /// <see cref="SpecName"/> accepts (<c>GRIB2</c>, <c>NetCDF</c>, <c>CAP</c>).
    /// </summary>
    public bool IsS100(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return true;
        return TryResolve(spec, out var registration)
            ? registration.IsS100
            : !SpecName.IsNonS100Format(spec);
    }

    /// <summary>
    /// What <paramref name="spec"/> is drawn from
    /// (<see cref="ProductRegistration.Portrayal"/>);
    /// <see cref="PortrayalBasis.Catalogue"/> for an unregistered or blank spec.
    /// </summary>
    public PortrayalBasis PortrayalOf(string? spec) =>
        !string.IsNullOrWhiteSpace(spec) && TryResolve(spec, out var registration)
            ? registration.Portrayal
            : PortrayalBasis.Catalogue;

    /// <summary>
    /// Returns the portrayal specification for <paramref name="product"/>: the
    /// registration's <see cref="ProductRegistration.PortrayalSpec"/> (edition
    /// unspecified) when it names one — <c>"S-101"</c> for legacy S-57 —
    /// otherwise <paramref name="product"/> unchanged. Prefer
    /// <see cref="IDatasetProcessor.PortrayalSpec"/> when a processor is at hand;
    /// use this where a caller has only the product spec.
    /// </summary>
    /// <param name="product">The dataset's declared product specification.</param>
    public SpecRef PortrayalSpecFor(SpecRef product)
        => product.Name is { } name
            && TryResolve(name, out var registration)
            && registration.PortrayalSpec is { } portrayal
                ? new SpecRef(portrayal, default)
                : product;

    /// <summary>
    /// String overload of <see cref="PortrayalSpecFor(SpecRef)"/> for callers
    /// that hold only the canonical product-spec code (e.g. the viewer's
    /// <c>DatasetEntry.ProductSpec</c>): maps <c>"S-57"</c> to <c>"S-101"</c>
    /// and returns every other code unchanged.
    /// </summary>
    /// <param name="productSpecName">The canonical product-spec code.</param>
    public string PortrayalSpecName(string productSpecName)
    {
        ArgumentNullException.ThrowIfNull(productSpecName);
        return productSpecName.Length > 0
            && TryResolve(productSpecName, out var registration)
            && registration.PortrayalSpec is { } portrayal
                ? portrayal
                : productSpecName;
    }

    /// <inheritdoc />
    S98DisplayPlane? IProductPlaneTable.FindDefaultPlane(string productSpec, string? layerKind) =>
        !string.IsNullOrWhiteSpace(productSpec) && TryResolve(productSpec, out var registration)
            ? registration.DefaultPlaneFor(layerKind)
            : null;

    /// <summary>
    /// Canonicalizes a spec key so registrations and look-ups agree regardless of
    /// the exact identifier form used. Recognized S-100 product identifiers
    /// (e.g. <c>"S101"</c>, <c>"s-101"</c>, <c>"  S-101  "</c>) collapse to their
    /// canonical <c>"S-101"</c> form via
    /// <see cref="DatasetPipelineFactory.MapProductIdentifierToSpec"/>, and a
    /// non-S-100 format name (<c>"grib2"</c>) to its canonical
    /// <see cref="SpecName"/> casing; any other identifier (a host's own
    /// product) is kept as-is apart from trimming, and the dictionary's
    /// case-insensitive comparer handles casing.
    /// </summary>
    internal static string NormalizeSpec(string spec)
    {
        ArgumentException.ThrowIfNullOrEmpty(spec);
        return DatasetPipelineFactory.MapProductIdentifierToSpec(spec)
            ?? (SpecName.IsNonS100Format(spec) ? SpecName.Normalize(spec) : spec.Trim());
    }
}
