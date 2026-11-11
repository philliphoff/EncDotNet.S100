namespace EncDotNet.S100.Datasets.S129.Fusion;

/// <summary>
/// The reason an <see cref="EncDotNet.S100.Datasets.S129.DataModel.S129ExternalReference"/>
/// could not be resolved against an in-process dataset.
/// </summary>
public enum S129ReferenceResolutionReason
{
    /// <summary>
    /// No candidate dataset of the expected product was supplied to
    /// <see cref="S129CrossProductResolver.Resolve"/>. The reference is
    /// preserved verbatim.
    /// </summary>
    DatasetNotProvided,

    /// <summary>
    /// A candidate dataset was supplied but its product did not match
    /// the kind expected by the reference.
    /// </summary>
    WrongProduct,

    /// <summary>
    /// A candidate dataset was supplied but its producer identifier
    /// (and optional version) did not match the textual identifier
    /// carried by the S-129 reference.
    /// </summary>
    IdentifierMismatch,

    /// <summary>
    /// The candidate dataset was structurally sound but lacked the
    /// referenced entity (e.g. an <c>xlink:href</c> targeting a
    /// <c>gml:id</c> not present in the candidate).
    /// </summary>
    ReferentMissing,
}
