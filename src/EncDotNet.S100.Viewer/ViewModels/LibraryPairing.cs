using System.Windows.Input;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A dataset paired with the selected one because it covers the same grid
/// cell — an S-111 tile's S-102 bathymetry (#685, handoff B7).
/// </summary>
/// <param name="Title">"Bathymetry for this tile".</param>
/// <param name="Detail">"S-102 102US004VA1DD · same grid cell · online · 3,1 MB".</param>
/// <param name="IsLocal">True when the paired dataset is on disk already (no Get button).</param>
/// <param name="GetCommand">Downloads the paired dataset.</param>
internal sealed record LibraryPairing(string Title, string Detail, bool IsLocal, ICommand GetCommand)
{
    /// <summary>True when the Get button is shown.</summary>
    public bool CanGet => !IsLocal;
}
