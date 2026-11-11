namespace EncDotNet.S100.Testing.Scenarios.Protection;

/// <summary>What the permit for one dataset says, and so what a reader should conclude.</summary>
public enum PermitKind
{
    /// <summary>A permit for the dataset's edition, expiring in 2099: <c>Allowed</c>.</summary>
    Valid,

    /// <summary>No permit for the dataset: <c>PermitNotFound</c>.</summary>
    Omitted,

    /// <summary>A permit for edition 2 while the catalogue has edition 1: <c>EditionMismatch</c>.</summary>
    WrongEdition,

    /// <summary>A permit tied to the day after the dataset's issue date, with no edition: <c>IssueDateMismatch</c>.</summary>
    WrongIssueDate,

    /// <summary>A permit that expired the day before the dataset was issued: <c>IssuedAfterExpiry</c>.</summary>
    ExpiredBeforeIssue,

    /// <summary>
    /// A permit that expires 30 days after the dataset's issue date:
    /// <c>Allowed</c>, but due to expire (for the "expiring" UX).
    /// </summary>
    ExpiringSoon,

    /// <summary>
    /// A valid permit whose cell key is wrapped for another system's hardware
    /// ID: <c>Allowed</c>, but decryption fails with <c>DatasetDecryptionException</c>.
    /// </summary>
    WrongHardwareId,
}

/// <summary>How <c>PERMIT.XML</c> is signed.</summary>
public enum PermitSigning
{
    /// <summary>Signed by the data server, certified by the trusted Scheme Administrator.</summary>
    Trusted,

    /// <summary>Signed by the data server, certified by a Scheme Administrator nobody trusts: <c>CertificateUntrusted</c>.</summary>
    UntrustedSchemeAdministrator,

    /// <summary>Signed with a data-server certificate that expired on 30 June 2026: <c>CertificateExpired</c>.</summary>
    ExpiredCertificate,

    /// <summary>No <c>PERMIT.SIGN</c>: <c>NotSigned</c>.</summary>
    Unsigned,

    /// <summary><c>PERMIT.SIGN</c> signs different bytes from <c>PERMIT.XML</c>: <c>SignatureInvalid</c>.</summary>
    Tampered,
}

/// <summary>Part 15 handling of one dataset in a protected scenario.</summary>
/// <param name="DatasetName">The dataset's file name without extension, as the scenario writes it.</param>
public sealed record DatasetProtection(string DatasetName)
{
    /// <summary>
    /// Whether the dataset is encrypted. <c>false</c> leaves it readable but
    /// still signed in the catalogue.
    /// </summary>
    public bool Encrypt { get; init; } = true;

    /// <summary>The dataset's permit (encrypted datasets only).</summary>
    public PermitKind Permit { get; init; } = PermitKind.Valid;

    /// <summary>
    /// Leaves <c>dataStatus</c> off the dataset's catalogue signature, as some
    /// producers do (#754).
    /// </summary>
    public bool OmitSignatureDataStatus { get; init; }

    /// <summary>
    /// Writes the dataset as update 1 (<c>.001</c>, <c>purpose</c> <c>update</c>)
    /// without its base: <c>BaseDatasetMissing</c>. S-101 only. The content is
    /// the base cell's, encrypted: the permit check refuses it before anything
    /// reads it as an update.
    /// </summary>
    public bool AsUpdateWithoutBase { get; init; }
}

/// <summary>
/// How a scenario is protected (S-100 Part 15): which datasets are signed and
/// encrypted, what their permits say, and how the permit file is signed.
/// </summary>
/// <param name="Name">The protected scenario's name, such as <c>"protected-basic"</c>.</param>
/// <param name="Datasets">The signed datasets; any others stay plain and unsigned.</param>
public sealed record ProtectionSpec(string Name, IReadOnlyList<DatasetProtection> Datasets)
{
    /// <summary>How <c>PERMIT.XML</c> is signed.</summary>
    public PermitSigning PermitSigning { get; init; } = PermitSigning.Trusted;

    /// <summary>
    /// Writes <c>PERMIT.XML</c> and <c>PERMIT.SIGN</c> to
    /// <c>TEST-ONLY/separate-permit/</c> instead of the exchange set root, as a
    /// permit delivered apart from the data.
    /// </summary>
    public bool PermitOnly { get; init; }
}
