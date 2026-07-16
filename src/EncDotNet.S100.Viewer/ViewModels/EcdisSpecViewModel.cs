using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Represents a single product specification in the ECDIS display panel.
/// Holds a flat list of viewing-group checkboxes and a per-spec reset command.
/// </summary>
internal sealed class EcdisSpecViewModel : ViewModelBase
{
    private readonly EcdisDisplayState _state;

    public EcdisSpecViewModel(
        EcdisDisplayState state,
        string specCode,
        PortrayalCatalogue catalogue,
        EcdisLabelOverrideProvider? labels = null)
    {
        _state = state;
        SpecCode = specCode;

        // Build the flat VG list from the catalogue, capturing each
        // group's curated section id (when any) for grouping below.
        var items = new List<EcdisViewingGroupViewModel>();
        var sectionById = new Dictionary<int, string?>();
        foreach (var vg in catalogue.ViewingGroups)
        {
            if (int.TryParse(vg.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                string? overrideLabel = null;
                if (labels is not null && labels.TryGetLabel(specCode, id, out var resolved))
                {
                    overrideLabel = resolved;
                }

                string? sectionId = null;
                if (labels is not null && labels.TryGetSectionId(specCode, id, out var resolvedSection))
                {
                    sectionId = resolvedSection;
                }

                items.Add(new EcdisViewingGroupViewModel(
                    state,
                    specCode,
                    id,
                    vg.Description.Name,
                    vg.Description.DescriptionText,
                    overrideLabel));
                sectionById[id] = sectionId;
            }
        }
        ViewingGroups = items;
        Sections = BuildSections(items, sectionById, labels?.GetSections(specCode));

        ResetOverridesCommand = new RelayCommand(() => _state.ClearOverridesForSpec(specCode));
    }

    /// <summary>Product spec code (e.g. "S-101").</summary>
    public string SpecCode { get; }

    /// <summary>Flat list of viewing-group checkboxes.</summary>
    public IReadOnlyList<EcdisViewingGroupViewModel> ViewingGroups { get; }

    /// <summary>
    /// True when this spec contributes at least one toggleable viewing group.
    /// Specs whose portrayal catalogue declares no viewing groups (e.g.
    /// S-411, whose display is driven by the sea-ice display-mode selector
    /// rather than viewing-group filters) produce an empty section; the panel
    /// hides those entries so an otherwise content-less spec header is not
    /// shown.
    /// </summary>
    public bool HasViewingGroups => ViewingGroups.Count > 0;

    /// <summary>
    /// Viewing-group checkboxes grouped into curated, ordered
    /// subsections. Specs that declare no sections produce a single
    /// untitled section preserving the catalogue order.
    /// </summary>
    public IReadOnlyList<EcdisViewingGroupSectionViewModel> Sections { get; }

    /// <summary>
    /// Groups <paramref name="items"/> into ordered, titled sections.
    /// When <paramref name="declaredSections"/> is null or empty the
    /// result is a single untitled section in catalogue order (the
    /// historical flat behaviour for specs without curated sections).
    /// Otherwise sections are emitted in declared order (skipping
    /// empty ones), groups within each section are sorted by label,
    /// and any group without a known section is collected into a
    /// trailing "Other" section.
    /// </summary>
    private static IReadOnlyList<EcdisViewingGroupSectionViewModel> BuildSections(
        IReadOnlyList<EcdisViewingGroupViewModel> items,
        IReadOnlyDictionary<int, string?> sectionById,
        IReadOnlyList<EcdisLabelSection>? declaredSections)
    {
        if (declaredSections is null || declaredSections.Count == 0)
        {
            return new[] { new EcdisViewingGroupSectionViewModel(null, items) };
        }

        var bySection = new Dictionary<string, List<EcdisViewingGroupViewModel>>(StringComparer.OrdinalIgnoreCase);
        var leftovers = new List<EcdisViewingGroupViewModel>();
        var declaredIds = new HashSet<string>(
            declaredSections.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var sectionId = sectionById.GetValueOrDefault(item.Id);
            if (sectionId is not null && declaredIds.Contains(sectionId))
            {
                if (!bySection.TryGetValue(sectionId, out var list))
                {
                    list = new List<EcdisViewingGroupViewModel>();
                    bySection[sectionId] = list;
                }
                list.Add(item);
            }
            else
            {
                leftovers.Add(item);
            }
        }

        var result = new List<EcdisViewingGroupSectionViewModel>();
        foreach (var section in declaredSections)
        {
            if (!bySection.TryGetValue(section.Id, out var list) || list.Count == 0)
                continue;

            var ordered = list
                .OrderBy(v => v.DisplayLabel, System.StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            result.Add(new EcdisViewingGroupSectionViewModel(section.Label, ordered));
        }

        if (leftovers.Count > 0)
        {
            var ordered = leftovers
                .OrderBy(v => v.DisplayLabel, System.StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            result.Add(new EcdisViewingGroupSectionViewModel(Strings.EcdisPanel_SectionOther, ordered));
        }

        return result;
    }

    /// <summary>Number of user-hidden viewing groups for this spec.</summary>
    public int OverrideCount => _state.GetHidden(SpecCode).Count;

    /// <summary>Formatted override count label (e.g. "3 overrides").</summary>
    public string OverrideCountLabel =>
        OverrideCount > 0
            ? string.Format(Strings.EcdisPanel_OverrideCountFormat, OverrideCount)
            : string.Empty;

    /// <summary>True when at least one override is active.</summary>
    public bool HasOverrides => OverrideCount > 0;

    /// <summary>Clears all overrides for this spec.</summary>
    public ICommand ResetOverridesCommand { get; }

    /// <summary>
    /// Refreshes the override count and every VG checkbox.
    /// Called when the global state changes externally.
    /// </summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(OverrideCount));
        OnPropertyChanged(nameof(OverrideCountLabel));
        OnPropertyChanged(nameof(HasOverrides));
        foreach (var vg in ViewingGroups)
            vg.Refresh();
    }
}
