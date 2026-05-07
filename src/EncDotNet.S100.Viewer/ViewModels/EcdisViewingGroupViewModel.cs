using System.Collections.Generic;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Represents a single viewing group in the ECDIS display panel.
/// The checkbox state reflects whether the group is hidden in
/// <see cref="EcdisDisplayState"/>.
/// </summary>
internal sealed class EcdisViewingGroupViewModel : ViewModelBase
{
    private readonly EcdisDisplayState _state;
    private readonly string _specCode;

    public EcdisViewingGroupViewModel(
        EcdisDisplayState state,
        string specCode,
        int viewingGroupId,
        string name)
    {
        _state = state;
        _specCode = specCode;
        Id = viewingGroupId;
        Name = name;
    }

    /// <summary>Integer viewing-group id.</summary>
    public int Id { get; }

    /// <summary>Human-readable name from the portrayal catalogue.</summary>
    public string Name { get; }

    /// <summary>
    /// True when this viewing group is visible (not in the hidden set).
    /// Setting to false calls <see cref="EcdisDisplayState.HideViewingGroup"/>;
    /// setting to true calls <see cref="EcdisDisplayState.ShowViewingGroup"/>.
    /// </summary>
    public bool IsVisible
    {
        get => !_state.GetHidden(_specCode).Contains(Id);
        set
        {
            if (value)
                _state.ShowViewingGroup(_specCode, Id);
            else
                _state.HideViewingGroup(_specCode, Id);

            Telemetry.ViewingGroupToggled.Add(1,
                new KeyValuePair<string, object?>("s100.spec", _specCode),
                new KeyValuePair<string, object?>("s100.viewinggroup", Id));

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Refreshes the <see cref="IsVisible"/> binding from the current
    /// state (called when the global state changes externally).
    /// </summary>
    internal void Refresh() => OnPropertyChanged(nameof(IsVisible));
}
