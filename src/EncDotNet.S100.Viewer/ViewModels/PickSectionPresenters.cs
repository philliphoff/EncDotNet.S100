using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// How Object Information presents each <see cref="PickSection"/> kind: a
/// factory per kind that builds the section's view model, which
/// <c>PickReportView</c> shows through the data template for its type. A kind
/// with no presenter shows its rows (<see cref="PickSectionRowsViewModel"/>).
/// </summary>
internal sealed class PickSectionPresenters
{
    private readonly Dictionary<string, Func<PickSection, object?>> _byKind = new(StringComparer.Ordinal);

    /// <summary>The presenters SoundCharts uses, with the built-in kinds.</summary>
    public static PickSectionPresenters Default { get; } = CreateDefault();

    /// <summary>Creates presenters for the built-in kinds: the ice egg code.</summary>
    public static PickSectionPresenters CreateDefault()
    {
        var presenters = new PickSectionPresenters();
        presenters.Register(IceEggCodeSection.KindName,
            section => section is IceEggCodeSection egg ? new EggCodeViewModel(egg.Egg) : null);
        return presenters;
    }

    /// <summary>Registers the presenter of <paramref name="kind"/>, replacing any earlier one.</summary>
    /// <param name="kind">The section kind.</param>
    /// <param name="present">Builds the section's view model, or returns <see langword="null"/> to show its rows.</param>
    public void Register(string kind, Func<PickSection, object?> present)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(present);
        _byKind[kind] = present;
    }

    /// <summary>The view model of <paramref name="section"/>: its kind's presenter's, else its rows.</summary>
    /// <param name="section">The section.</param>
    public object Present(PickSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (_byKind.TryGetValue(section.Kind, out var present) && present(section) is { } viewModel)
            return viewModel;
        return new PickSectionRowsViewModel(
            PickSectionKindRegistry.Default.Find(section.Kind)?.Description ?? section.Kind,
            section.ToRows());
    }
}

/// <summary>
/// A <see cref="PickSection"/> whose kind has no presenter, shown as a
/// heading and its rows.
/// </summary>
/// <param name="Heading">The kind's description, or the kind itself.</param>
/// <param name="Rows">The section's rows.</param>
internal sealed record PickSectionRowsViewModel(string Heading, IReadOnlyList<PickAttribute> Rows);
