using Avalonia;
using Avalonia.Controls;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// A row's pane chips (<see cref="PaneChip"/>) in reading order, while the map
/// is split (split map step 2 §2.6, D18). The Datasets rows, the exchange-set
/// headers and the Layer stack rows show it in place of the eye.
/// </summary>
public partial class PaneChipStrip : UserControl
{
    /// <summary>Defines the <see cref="Chips"/> property.</summary>
    internal static readonly StyledProperty<IReadOnlyList<DatasetPaneChip>?> ChipsProperty =
        AvaloniaProperty.Register<PaneChipStrip, IReadOnlyList<DatasetPaneChip>?>(nameof(Chips));

    /// <summary>Defines the <see cref="ChipAutomationId"/> property.</summary>
    public static readonly StyledProperty<string?> ChipAutomationIdProperty =
        AvaloniaProperty.Register<PaneChipStrip, string?>(nameof(ChipAutomationId));

    /// <summary>Creates an empty strip.</summary>
    public PaneChipStrip() => InitializeComponent();

    /// <summary>The chips, one per pane.</summary>
    internal IReadOnlyList<DatasetPaneChip>? Chips
    {
        get => GetValue(ChipsProperty);
        set => SetValue(ChipsProperty, value);
    }

    /// <summary>
    /// The automation id every chip shares (<c>Datasets.Row.Chip</c>); tests
    /// and the <c>ui_*</c> tools scope it by the row and the chip's name ("Map 2").
    /// </summary>
    public string? ChipAutomationId
    {
        get => GetValue(ChipAutomationIdProperty);
        set => SetValue(ChipAutomationIdProperty, value);
    }
}
