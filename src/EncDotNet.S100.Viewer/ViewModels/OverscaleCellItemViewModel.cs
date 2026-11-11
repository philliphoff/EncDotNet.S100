namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A single row in the status-bar overscale popup (issue #441): a cell's display
/// name and its formatted overscale factor (e.g. <c>"4.6×"</c>).
/// </summary>
/// <param name="Name">The overscaled cell's display name.</param>
/// <param name="FactorText">The overscale factor, pre-formatted for display.</param>
internal sealed record OverscaleCellItemViewModel(string Name, string FactorText);
