using System.Collections.Generic;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Capability implemented by dataset processors whose portrayal catalogue
/// declares one or more S-100 Part 9 §11.7 display modes — alternative looks
/// selectable over a single dataset (e.g. S-411 sea ice offers concentration,
/// stage-of-development and navigational modes). Lets callers (e.g. the CLI
/// <c>info</c> command) enumerate the available modes without reflecting over
/// concrete processor or catalogue types.
/// </summary>
public interface IDisplayModeAwareDatasetProcessor
{
    /// <summary>
    /// The set of display-mode ids declared by the dataset's portrayal
    /// catalogue, in no particular order. Empty when the catalogue declares no
    /// display modes (the common case; portrayal then always uses the single
    /// default look).
    /// </summary>
    IReadOnlyCollection<string> DeclaredDisplayModeIds { get; }
}
