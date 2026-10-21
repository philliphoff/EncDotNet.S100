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

    /// <summary>Raised after <see cref="Warnings"/>, <see cref="WarningSet"/> or the checked route's warnings (<see cref="RouteFindingFor"/>) change.</summary>
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

    /// <summary>
    /// The mariner's classification of a listed part's warning (#996); On
    /// chart by default. A change raises <see cref="WarningsChanged"/>.
    /// </summary>
    NavigationalWarningClassification ClassificationOf(NavigationalWarningEntry entry) => NavigationalWarningClassification.OnChart;

    /// <summary>Classifies a listed part's warning, as the panel's row does (#996).</summary>
    /// <returns>Whether it changed; false by default.</returns>
    bool SetClassification(NavigationalWarningEntry entry, NavigationalWarningClassification classification) => false;

    /// <summary>
    /// The checked route's S-124 finding for a listed part (#959), which the
    /// card's "Crosses route · leg 3 → 4" row opens; <c>null</c> when the part
    /// is off the route or Check route is off, and by default.
    /// </summary>
    EncDotNet.S100.Navigation.RouteFinding? RouteFindingFor(NavigationalWarningEntry entry) => null;

    /// <summary>Selects a finding from <see cref="RouteFindingFor"/> in the Findings list and brings the list forward.</summary>
    /// <returns>Whether it opened; false by default.</returns>
    bool OpenRouteFinding(EncDotNet.S100.Navigation.RouteFinding finding) => false;
}
