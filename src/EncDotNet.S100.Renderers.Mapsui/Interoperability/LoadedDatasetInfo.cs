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
/// Whether this dataset is currently active for display. The Active
/// flag is in-memory only (PR-L3); the Layer Controls UI toggles it
/// via <c>IDatasetLoaderService.SetActive</c>. A rule that suppresses
/// S-101 features when S-102 is loaded must only fire when the S-102
/// dataset is <c>Active</c>; an inactive S-102 must not suppress
/// S-101.
/// <para>
/// <strong>Active vs <c>DatasetEntry.IsVisible</c>.</strong> The two
/// flags are independent and serve different layers of the stack:
/// </para>
/// <list type="bullet">
///   <item>
///     <c>DatasetEntry.IsVisible</c> is a purely visual toggle that
///     hides / shows the rendered Mapsui layers without affecting
///     anything else — useful for quickly peeking at what lies
///     beneath one dataset.
///   </item>
///   <item>
///     <c>Active</c> controls whether the dataset participates in
///     S-98 inter-product rule evaluation (e.g. S-102 suppressing
///     S-101 depth features) and in pick. An inactive dataset is
///     fully removed from the cross-product stack — its layers
///     don't paint and its presence doesn't influence sibling
///     products. PR-L4 will persist Active in <c>ViewerSettings</c>.
///   </item>
/// </list>
/// </param>
public sealed record LoadedDatasetInfo(string DatasetId, string ProductSpec, bool Active);
