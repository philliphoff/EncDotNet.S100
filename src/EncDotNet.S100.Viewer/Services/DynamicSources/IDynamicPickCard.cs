namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// A rich Object information card for a live feature, presented from its pick
/// section: the predicted tide and current station cards (tide predictions
/// design D8, data source design note slice 7d).
/// </summary>
internal interface IDynamicPickCard
{
    /// <summary>Stops the card following the view time; called when the pick is replaced.</summary>
    void Release();

    /// <summary>
    /// An area the pick highlight outlines for the feature, such as a tide
    /// station's cell (tide predictions design D13); null for none.
    /// </summary>
    IReadOnlyList<EncDotNet.S100.DataModel.GeoPosition>? Outline { get; }
}
