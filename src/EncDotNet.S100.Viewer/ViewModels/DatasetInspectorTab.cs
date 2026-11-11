namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The tabs of the Datasets panel's pinned dataset inspector, in the order
/// they appear (the value is the tab's index).
/// </summary>
internal enum DatasetInspectorTab
{
    /// <summary>The Dataset tab: spec, time and dataset-wide settings.</summary>
    Dataset = 0,

    /// <summary>The Layers tab: the dataset's sub-layers.</summary>
    Layers = 1,

    /// <summary>The Validation tab: the rule pack's findings.</summary>
    Validation = 2,
}
