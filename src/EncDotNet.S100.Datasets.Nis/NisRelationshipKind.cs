namespace EncDotNet.S100.Datasets.Nis;

/// <summary>
/// How the destination of an NIS feature relationship relates to its source,
/// the S-57 relationship indicator (RIND, S-57 Part 3 §7.6.8).
/// </summary>
public enum NisRelationshipKind
{
    /// <summary>The destination is the master of the source.</summary>
    Master = 1,

    /// <summary>The destination is a slave of the source, such as a topmark on a beacon.</summary>
    Slave = 2,

    /// <summary>The destination is a peer, such as a member of a collection.</summary>
    Peer = 3,
}
