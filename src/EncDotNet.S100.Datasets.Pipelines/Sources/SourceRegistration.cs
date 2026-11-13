using System.Diagnostics.CodeAnalysis;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// A data source that isn't a file, such as a live feed or a polled web
/// service: what it's called, what it reports as, when a host runs it, and how
/// to build its processor. The sibling of <see cref="ProductRegistration"/>,
/// which covers products found by detecting a file.
/// </summary>
/// <remarks>
/// <para>
/// A source is configured rather than detected: with a URL, a key, a serial
/// port or an input spec, in an options record (<see cref="OptionsType"/>).
/// A host keeps the options with the dataset entry, and passes them back in
/// <see cref="SourceCreationContext.Options"/> when it builds the processor.
/// </para>
/// <para>
/// Register sources in a <see cref="SourceRegistry"/> at start-up, in code, so
/// the trim analyzers see them.
/// </para>
/// </remarks>
public sealed record SourceRegistration
{
    /// <summary>
    /// The source's id, a dotted string such as <c>"ndbc.realtime"</c>. A
    /// host saves it with each dataset entry built from the source. Built-in
    /// sources have no owner prefix; other sources start with their owner's
    /// name (<c>"acme.ice-feed"</c>).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The name a host shows for the source, such as <c>"NDBC observations"</c>.</summary>
    public required string DisplayName { get; init; }

    /// <summary>One line on what the source shows, for a host's source list. Optional.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// The product name the source's processor reports as, such as
    /// <c>NDBC</c>; a <see cref="SpecName"/> format name for a source that
    /// isn't an S-100 product.
    /// </summary>
    public required string Spec { get; init; }

    /// <summary>Whether the source's data is an S-100 product. <see langword="false"/> for every built-in source.</summary>
    public bool IsS100 { get; init; }

    /// <summary>When a host runs the source. Defaults to <see cref="SourceActivation.Always"/>.</summary>
    public SourceActivation Activation { get; init; } = SourceActivation.Always;

    /// <summary>
    /// The options record a host stores and passes to <see cref="Create"/>,
    /// or <see langword="null"/> for a source with no options. A host can
    /// build a settings page from its public properties.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type? OptionsType { get; init; }

    /// <summary>
    /// The options a new dataset entry starts with, an instance of
    /// <see cref="OptionsType"/>. <see langword="null"/> when the source has
    /// no options or has no usable defaults.
    /// </summary>
    public object? DefaultOptions { get; init; }

    /// <summary>
    /// Builds the source's processor. The processor owns the source, so
    /// disposing it stops the source. A host starts it with
    /// <see cref="IHostedDatasetProcessor.Start"/> once it's shown.
    /// </summary>
    public required Func<SourceCreationContext, IDatasetProcessor> Create { get; init; }

    /// <summary>
    /// Pick contributors for this source's features only. A host applies them
    /// before the global ones in <see cref="PickContributorRegistry"/>.
    /// </summary>
    public IReadOnlyList<IPickContributor> PickContributors { get; init; } = [];
}
