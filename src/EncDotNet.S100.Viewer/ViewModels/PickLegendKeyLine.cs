using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.ViewModels.Legends;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A key line in Object information (#1410, legends design note D3, D16): the
/// Legend row a picked feature is drawn with, "S-102 · 2–10 m" with its swatch,
/// and an <b>In legend</b> link that switches to the Legend tab, expands the
/// section and rings the row.
/// </summary>
internal sealed class PickLegendKeyLine
{
    /// <summary>Creates the line for a matched row.</summary>
    /// <param name="match">The row the feature is drawn with.</param>
    /// <param name="tile">The tile the glyph sits on: the palette's chart background.</param>
    /// <param name="show">Opens the Legend tab at a section and row (<see cref="LegendService.Show"/>).</param>
    public PickLegendKeyLine(LegendRowMatch match, IBrush tile, Action<string?, string?> show)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(show);
        SectionId = match.Section.Id;
        RowLabel = match.Row.Label;
        Row = new LegendRowViewModel(match.Row, tile);
        Text = string.Format(CultureInfo.CurrentCulture, Strings.Pick_LegendKeyLineFormat, match.Product, RowLabel);
        InLegendName = string.Format(CultureInfo.CurrentCulture, Strings.Pick_InLegendNameFormat, Text);
        ShowInLegendCommand = new RelayCommand(() => show(SectionId, RowLabel));
    }

    /// <summary>The Legend section the row is in.</summary>
    public string SectionId { get; }

    /// <summary>The row's label, which the Legend tab rings.</summary>
    public string RowLabel { get; }

    /// <summary>The row's glyph, as the Legend tab draws it.</summary>
    public LegendRowViewModel Row { get; }

    /// <summary>The line: product and row, "S-102 · 2–10 m".</summary>
    public string Text { get; }

    /// <summary>The <b>In legend</b> link's accessible name and tooltip, "Show S-102 · 2–10 m in the legend".</summary>
    public string InLegendName { get; }

    /// <summary>Opens the Legend tab at the row.</summary>
    public ICommand ShowInLegendCommand { get; }
}
