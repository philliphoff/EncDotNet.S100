namespace EncDotNet.S100.Datasets.Nis;

/// <summary>One cell of an NIS, translated and written as an S-57 base cell.</summary>
/// <param name="DatasetName">The source cell name (<c>DSNM</c>), such as <c>US5CA98M.005</c>.</param>
/// <param name="Path">The S-57 base cell (<c>.000</c>) the cell was written to.</param>
public sealed record NisCellFile(string DatasetName, string Path);
