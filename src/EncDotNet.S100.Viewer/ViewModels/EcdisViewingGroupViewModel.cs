using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
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
        string name,
        string? description = null,
        string? overrideLabel = null)
    {
        _state = state;
        _specCode = specCode;
        Id = viewingGroupId;
        Name = name;
        Description = description;
        DisplayLabel = ResolveDisplayLabel(viewingGroupId, name, description, overrideLabel);
        Tooltip = BuildTooltip(viewingGroupId, name, description);
    }

    /// <summary>Integer viewing-group id.</summary>
    public int Id { get; }

    /// <summary>
    /// Raw name as authored in the Portrayal Catalogue. Retained for
    /// telemetry and tests; the UI binds to <see cref="DisplayLabel"/>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Raw description as authored in the Portrayal Catalogue, if any.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Human-friendly label shown to the user. Resolved from (in
    /// order): the viewer's curated override file, the raw PC name
    /// (when it is not just the numeric id), the raw PC description,
    /// or a synthesised <c>Viewing group &lt;id&gt;</c> fallback.
    /// </summary>
    public string DisplayLabel { get; }

    /// <summary>
    /// Tooltip text combining the numeric id with the raw PC name
    /// and description, preserving the symbol/feature acronyms that
    /// power users rely on.
    /// </summary>
    public string Tooltip { get; }

    private static string ResolveDisplayLabel(int id, string name, string? description, string? overrideLabel)
    {
        if (!string.IsNullOrWhiteSpace(overrideLabel))
        {
            return overrideLabel.Trim();
        }

        var trimmedName = name?.Trim();
        if (!string.IsNullOrEmpty(trimmedName) && !IsNumericName(trimmedName, id))
        {
            return trimmedName;
        }

        var trimmedDescription = description?.Trim();
        if (!string.IsNullOrEmpty(trimmedDescription))
        {
            return trimmedDescription;
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            Strings.EcdisPanel_SynthesizedLabelFormat,
            id.ToString(CultureInfo.InvariantCulture));
    }

    private static bool IsNumericName(string name, int id)
    {
        // Treat any purely-numeric PC name as not useful for display.
        // Catches both S-127 (where name == id, e.g. "31010" for id
        // 31010) and S-421 (where name is the IEC reference number
        // e.g. "52000" for id 1).
        _ = id;
        return int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static string BuildTooltip(int id, string name, string? description)
    {
        var builder = new StringBuilder();
        builder.Append('#');
        builder.Append(id.ToString(CultureInfo.InvariantCulture));

        var trimmedName = name?.Trim();
        if (!string.IsNullOrEmpty(trimmedName) && !IsNumericName(trimmedName, id))
        {
            builder.Append(" — ");
            builder.Append(trimmedName);
        }

        var trimmedDescription = description?.Trim();
        if (!string.IsNullOrEmpty(trimmedDescription))
        {
            builder.AppendLine();
            builder.Append(trimmedDescription);
        }

        return builder.ToString();
    }

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
