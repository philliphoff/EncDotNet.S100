using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The controls over and around the map that change what it draws: the
/// display category and text group toolbars, the display mode selector, the
/// display combinations behind the <b>View ▾</b> pill, and the navigational
/// warnings' in-map note and "Show warnings not yet in force" switch.
/// </summary>
internal sealed class MapChromeViewModel
{
    public MapChromeViewModel(
        DisplayToolbarViewModel displayToolbar,
        TextGroupToolbarViewModel textToolbar,
        DisplayModeToolbarViewModel displayModeToolbar,
        DisplayCombinationsViewModel displayCombinations,
        NavigationalWarningsViewModel navigationalWarnings,
        NavigationalWarningDrawingService navigationalWarningDrawing)
    {
        ArgumentNullException.ThrowIfNull(displayToolbar);
        ArgumentNullException.ThrowIfNull(textToolbar);
        ArgumentNullException.ThrowIfNull(displayModeToolbar);
        ArgumentNullException.ThrowIfNull(displayCombinations);
        ArgumentNullException.ThrowIfNull(navigationalWarnings);
        ArgumentNullException.ThrowIfNull(navigationalWarningDrawing);

        DisplayToolbar = displayToolbar;
        TextToolbar = textToolbar;
        DisplayModeToolbar = displayModeToolbar;
        DisplayCombinations = displayCombinations;
        NavigationalWarnings = navigationalWarnings;
        NavigationalWarningDrawing = navigationalWarningDrawing;
    }

    /// <summary>The display category buttons: Display base, Standard, Other and All.</summary>
    public DisplayToolbarViewModel DisplayToolbar { get; }

    /// <summary>The text group buttons: important text, other text and all text.</summary>
    public TextGroupToolbarViewModel TextToolbar { get; }

    /// <summary>The display mode selector on the map's toolbar.</summary>
    public DisplayModeToolbarViewModel DisplayModeToolbar { get; }

    /// <summary>The display combinations behind the map's <b>View ▾</b> pill.</summary>
    public DisplayCombinationsViewModel DisplayCombinations { get; }

    /// <summary>The Navigational warnings panel (#932), whose in-map note the window shows over the map.</summary>
    public NavigationalWarningsViewModel NavigationalWarnings { get; }

    /// <summary>Which S-124 warnings the map draws, and the "Show warnings not yet in force" setting (#993).</summary>
    public NavigationalWarningDrawingService NavigationalWarningDrawing { get; }
}
