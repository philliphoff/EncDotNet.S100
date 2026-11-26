namespace EncDotNet.S100.Datasets.Nis;

/// <summary>
/// A relationship between two NIS features, named by their S-57 long names:
/// one row of the NIS <c>PLTS_FREL</c> table, which becomes an S-57 feature
/// pointer (FFPT) on the source.
/// </summary>
/// <param name="SourceLongName">The long name (<c>LNAM</c>) of the feature the pointer is on.</param>
/// <param name="DestinationLongName">The long name of the feature it points to.</param>
/// <param name="Kind">How the destination relates to the source.</param>
public sealed record NisRelationship(string SourceLongName, string DestinationLongName, NisRelationshipKind Kind);
