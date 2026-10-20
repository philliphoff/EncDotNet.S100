using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// What a Warning card (in Object Information, or inline in the findings
/// list) needs from the Navigational
/// warnings panel (#932): the listed warnings its text may link to, opening
/// one as the panel's row does, and framing one on the map.
/// </summary>
internal interface INavigationalWarningNavigator
{
    /// <summary>Every listed warning part.</summary>
    IReadOnlyList<NavigationalWarningEntry> Warnings { get; }

    /// <summary>
    /// Every loaded warning, judged together, so a card's status agrees with
    /// the panel's (#992); an empty set by default.
    /// </summary>
    Datasets.Pipelines.NavigationalWarningSet WarningSet => Datasets.Pipelines.NavigationalWarningSet.Empty;

    /// <summary>Raised after <see cref="Warnings"/> or <see cref="WarningSet"/> changes.</summary>
    event EventHandler? WarningsChanged;

    /// <summary>Finds a listed part by dataset id and feature id.</summary>
    NavigationalWarningEntry? Find(string datasetId, string featureRef);

    /// <summary>Opens a part as selecting its row in the panel does.</summary>
    /// <returns>Whether it opened.</returns>
    bool Select(NavigationalWarningEntry entry);

    /// <summary>
    /// Opens a part as <see cref="Select"/> does and brings the Navigational
    /// warnings panel forward, for a card shown outside it (a finding's
    /// inline Warning card, #959 E1).
    /// </summary>
    /// <returns>Whether it opened.</returns>
    bool Reveal(NavigationalWarningEntry entry) => Select(entry);

    /// <summary>
    /// Centres the map on a part at a scale that fits its geometry, as
    /// selecting its row does, without changing the pick.
    /// </summary>
    /// <returns>Whether the part has a location to frame.</returns>
    bool ZoomTo(NavigationalWarningEntry entry);

    /// <summary>
    /// Whether a listed part's warning is new since its source's latest
    /// refresh (the card header's NEW); false by default.
    /// </summary>
    bool IsNew(NavigationalWarningEntry entry) => false;
}
