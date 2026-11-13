namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The <see cref="IPickContributor"/>s that add <see cref="PickSection"/>s to
/// picked features from any product or source, and the step that runs them.
/// </summary>
/// <remarks>
/// <para>
/// Hosts call <see cref="Apply"/> on the result of
/// <see cref="IDatasetProcessor.GetFeatureInfo"/> (SoundCharts' pick report,
/// the map session's pick, <c>S100FeatureCatalogue</c>), and MCP calls
/// <see cref="Contribute"/> for features it reads from the catalogue, so a
/// contribution shows up in each of them.
/// </para>
/// <para>
/// A feature has at most one section per kind. Sections the processor set
/// itself win, then contributors in registration order. Register contributors
/// at start-up, before the first pick.
/// </para>
/// </remarks>
public sealed class PickContributorRegistry
{
    private readonly List<IPickContributor> _contributors = [];
    private readonly object _lock = new();

    /// <summary>The process-wide registry, pre-loaded with the built-in contributors.</summary>
    public static PickContributorRegistry Default { get; } = CreateDefault();

    /// <summary>Creates a registry with the built-in contributors: the ice egg code.</summary>
    public static PickContributorRegistry CreateDefault()
    {
        var registry = new PickContributorRegistry();
        registry.Add(IceEggCodeContributor.Instance);
        return registry;
    }

    /// <summary>The registered contributors, in registration order.</summary>
    public IReadOnlyList<IPickContributor> Contributors
    {
        get
        {
            lock (_lock)
                return [.. _contributors];
        }
    }

    /// <summary>Adds a contributor after the ones already registered.</summary>
    /// <param name="contributor">The contributor.</param>
    public void Add(IPickContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        lock (_lock)
            _contributors.Add(contributor);
    }

    /// <summary>
    /// <paramref name="info"/> with the sections the contributors add for it,
    /// or <paramref name="info"/> itself when they add none.
    /// </summary>
    /// <param name="info">The feature info from <paramref name="processor"/>.</param>
    /// <param name="processor">The processor the feature was picked from.</param>
    public FeatureInfo Apply(FeatureInfo info, IDatasetProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(processor);

        var context = new PickContext
        {
            Spec = processor.Spec,
            FeatureRef = info.FeatureRef,
            FeatureType = info.FeatureType,
            Attributes = info.Attributes,
            Processor = processor,
            ResolveListedValueDefinition = processor is IListedValueDefinitions definitions
                ? definitions.ResolveListedValueDefinition
                : null,
        };
        var sections = Contribute(context, info.Sections);
        return ReferenceEquals(sections, info.Sections) ? info : info.WithSections(sections);
    }

    /// <summary>
    /// <paramref name="existing"/> followed by the sections the contributors add
    /// for kinds it doesn't have yet; <paramref name="existing"/> itself when
    /// they add none.
    /// </summary>
    /// <param name="context">The picked feature.</param>
    /// <param name="existing">The sections the feature already has.</param>
    public IReadOnlyList<PickSection> Contribute(PickContext context, IReadOnlyList<PickSection>? existing = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        existing ??= [];

        List<PickSection>? result = null;
        foreach (var contributor in Contributors)
        {
            if (contributor.Contribute(context) is not { } section)
                continue;
            if (HasKind(existing, section.Kind) || (result is not null && HasKind(result, section.Kind)))
                continue;
            (result ??= [.. existing]).Add(section);
        }

        return result ?? existing;
    }

    private static bool HasKind(IReadOnlyList<PickSection> sections, string kind)
    {
        foreach (var section in sections)
        {
            if (string.Equals(section.Kind, kind, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
