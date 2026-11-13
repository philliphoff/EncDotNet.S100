using EncDotNet.S100.Renderers.Mapsui.DynamicSources;

namespace EncDotNet.S100.Viewer.Services.DynamicSources;

/// <summary>
/// A rich Object information card for a dynamic feature, shown in place of
/// the generic list of values: the predicted tide station card (tide
/// predictions design D8).
/// </summary>
internal interface IDynamicPickCard
{
    /// <summary>Stops the card following the view time; called when the pick is replaced.</summary>
    void Release();
}

/// <summary>Makes the <see cref="IDynamicPickCard"/> of a picked dynamic feature.</summary>
internal interface IDynamicPickCardFactory
{
    /// <summary>The card for <paramref name="hit"/>, or <c>null</c> when this factory has none for it.</summary>
    /// <param name="hit">The picked feature and its source.</param>
    IDynamicPickCard? CreateCard(DynamicSourceHit hit);
}
