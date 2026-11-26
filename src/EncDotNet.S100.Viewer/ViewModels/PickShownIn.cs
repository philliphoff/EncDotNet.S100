using System.Windows.Input;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// An Object information chip saying whether a map of a split draws the
/// picked feature (split map design note D11): "Shown in Map 1", "Hidden in
/// Map 2". Clicking a hidden chip selects the feature's dataset in Datasets.
/// </summary>
/// <param name="Text">The chip's text.</param>
/// <param name="IsShown">Whether the map draws the feature.</param>
/// <param name="AutomationId">The chip's AutomationId, after its map.</param>
/// <param name="Command">For a hidden chip, selects the dataset; else <see langword="null"/>.</param>
internal sealed record PickShownInChip(string Text, bool IsShown, string AutomationId, ICommand? Command)
{
    /// <summary>Whether the chip can be clicked.</summary>
    public bool IsHidden => !IsShown;
}

/// <summary>
/// A row of the Object information "Other maps" block (D11): another map of
/// the split whose time isn't the picked map's.
/// </summary>
/// <param name="Text">The map and its time, e.g. "Map 1 · 08:06".</param>
/// <param name="AutomationId">The row's AutomationId, after its map.</param>
internal sealed record PickOtherMapRow(string Text, string AutomationId);
