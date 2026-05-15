using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        // Build flat VG list from the catalogue's ViewingGroups collection.
        var items = new List<EcdisViewingGroupViewModel>();
        foreach (var vg in catalogue.ViewingGroups)
        {
            if (int.TryParse(vg.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                string? overrideLabel = null;
                if (labels is not null && labels.TryGetLabel(specCode, id, out var resolved))
                {
                    overrideLabel = resolved;
                }
                items.Add(new EcdisViewingGroupViewModel(
                    state,
                    specCode,
                    id,
                    vg.Description.Name,
                    vg.Description.DescriptionText,
                    overrideLabel));
            }
        }
        ViewingGroups = items;

        ResetOverridesCommand = new RelayCommand(() => _state.ClearOverridesForSpec(specCode));
    }

    /// <summary>Product spec code (e.g. "S-101").</summary>
    public string SpecCode { get; }

    /// <summary>Flat list of viewing-group checkboxes.</summary>
    public IReadOnlyList<EcdisViewingGroupViewModel> ViewingGroups { get; }

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
