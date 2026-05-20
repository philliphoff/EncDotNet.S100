namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Identifies a currently-loaded dataset to the S-98 interoperability
/// rule engine. Carries the minimum information a rule needs to
/// decide whether it should fire: which product spec the dataset
/// conforms to and whether the dataset is currently active for
/// display.
/// </summary>
/// <param name="DatasetId">
/// Stable identifier matching <see cref="LayerStackEntry.SourceDatasetId"/>
/// (typically the dataset's file name or exchange-set relative path).
/// </param>
/// <param name="ProductSpec">
/// Product specification name (e.g. <c>"S-101"</c>, <c>"S-102"</c>).
/// Matches <see cref="EncDotNet.S100.Core.SpecRef.Name"/>.
/// </param>
/// <param name="Active">
/// Whether this dataset is currently active for display. PR-L2
/// treats "loaded" as "active" (PR-L1 has no separate active/inactive
/// concept); the field is wired through so PR-L3's Layer Controls UI
/// can flip individual datasets off without re-loading. A rule that
/// suppresses S-101 features when S-102 is loaded must only fire
/// when the S-102 dataset is <c>Active</c>; an inactive S-102 must
/// not suppress S-101.
/// </param>
public sealed record LoadedDatasetInfo(string DatasetId, string ProductSpec, bool Active);
