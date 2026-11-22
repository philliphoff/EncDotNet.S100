namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// One result of <see cref="ChartReferenceIndex.Search"/>.
/// </summary>
/// <param name="Entry">The entry that matched.</param>
/// <param name="Match">How it matched, which decides its rank.</param>
public sealed record ReferenceHit(ReferenceEntry Entry, ReferenceMatch Match);
