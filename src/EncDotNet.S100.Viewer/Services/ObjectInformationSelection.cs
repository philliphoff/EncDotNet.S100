using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// What Object Information shows (#1397): the selected hit and the open
/// Warning card. The pick report publishes it; the Navigational warnings
/// panel follows the hit (a warning picked on the map is selected in its
/// list), and the warning drawing keeps the open card's warning drawn.
/// Nothing else has to hold the pick report to know.
/// </summary>
internal sealed class ObjectInformationSelection
{
    /// <summary>The hit Object Information shows, or <see langword="null"/>.</summary>
    public PickHit? Hit { get; private set; }

    /// <summary>The hit of the open Warning card, or <see langword="null"/> when none is open.</summary>
    public PickHit? OpenWarning { get; private set; }

    /// <summary>Raised when <see cref="Hit"/> changes, or Object Information is cleared or shown again.</summary>
    public event EventHandler? HitChanged;

    /// <summary>Raised when <see cref="OpenWarning"/> may have changed.</summary>
    public event EventHandler? OpenWarningChanged;

    /// <summary>Publishes the selected hit (raises <see cref="HitChanged"/> even when it is the same, as a pick shown again is).</summary>
    /// <param name="hit">The hit Object Information shows.</param>
    public void SetHit(PickHit? hit)
    {
        Hit = hit;
        HitChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Publishes the open Warning card's hit.</summary>
    /// <param name="openWarning">The hit of the open Warning card, or <see langword="null"/>.</param>
    public void SetOpenWarning(PickHit? openWarning)
    {
        OpenWarning = openWarning;
        OpenWarningChanged?.Invoke(this, EventArgs.Empty);
    }
}
