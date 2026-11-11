namespace EncDotNet.S100.Collections.Secom;

/// <summary>The SECOM (IEC 63173-2) interface version a service answers on.</summary>
public enum SecomApiVersion
{
    /// <summary>Edition 1 interfaces, under <c>/v1</c>.</summary>
    V1 = 1,

    /// <summary>Edition 2 interfaces, under <c>/v2</c>.</summary>
    V2 = 2,
}

/// <summary>The SECOM <c>containerType</c> of a data object.</summary>
public enum SecomContainerType
{
    /// <summary>A single S-100 dataset (<c>S100_DataSet</c>, 0).</summary>
    DataSet = 0,

    /// <summary>An S-100 exchange set (<c>S100_ExchangeSet</c>, 1).</summary>
    ExchangeSet = 1,

    /// <summary>Not an S-100 container (<c>NONE</c>, 2).</summary>
    None = 2,
}

/// <summary>
/// One entry of a SECOM <c>GetSummary</c> response: a data object the
/// service offers, described without its data.
/// </summary>
/// <param name="DataReference">The object's reference (a UUID), passed to <c>Get</c>.</param>
/// <param name="ProductSpec">
/// The data product in canonical short form (<c>"S-124"</c>), whatever form the
/// service used (<c>S124</c> or <c>S-124</c>); <see langword="null"/> when absent.
/// </param>
/// <param name="ProductVersion">The product specification edition (<c>info_productVersion</c>).</param>
/// <param name="ContainerType">Whether the object is a dataset or an exchange set.</param>
/// <param name="Identifier">The object's identifier (<c>info_identifier</c>).</param>
/// <param name="Name">The object's name (<c>info_name</c>).</param>
/// <param name="Status">The object's status as the service states it (<c>info_status</c>).</param>
/// <param name="Description">A description (<c>info_description</c>).</param>
/// <param name="LastModified">When the object last changed (<c>info_lastModifiedDate</c>).</param>
/// <param name="Size">The data size in bytes (<c>info_size</c>).</param>
/// <param name="DataProtection">True when the data is encrypted for its recipient.</param>
/// <param name="DataCompression">True when the data is compressed.</param>
public sealed record SecomSummary(
    string DataReference,
    string? ProductSpec,
    string? ProductVersion,
    SecomContainerType ContainerType,
    string? Identifier,
    string? Name,
    string? Status,
    string? Description,
    DateTimeOffset? LastModified,
    long? Size,
    bool DataProtection,
    bool DataCompression);

/// <summary>One page of a <c>GetSummary</c> response.</summary>
/// <param name="Items">The page's entries.</param>
/// <param name="TotalItems">How many entries match in all, when the service says.</param>
/// <param name="MaxItemsPerPage">The page size the service used, when it says.</param>
public sealed record SecomSummaryPage(IReadOnlyList<SecomSummary> Items, int? TotalItems, int? MaxItemsPerPage);

/// <summary>Every <c>GetSummary</c> entry read across pages.</summary>
/// <param name="Items">The entries, once per data reference.</param>
/// <param name="TotalItems">How many entries the service said match, if it said.</param>
/// <param name="Truncated">True when reading stopped at the cap with more entries left.</param>
public sealed record SecomSummaryList(IReadOnlyList<SecomSummary> Items, int? TotalItems, bool Truncated);

/// <summary>A SECOM <c>Capability</c> response.</summary>
/// <param name="ApiVersion">The interface version that answered.</param>
/// <param name="Entries">One entry per product the service offers.</param>
public sealed record SecomCapability(SecomApiVersion ApiVersion, IReadOnlyList<SecomCapabilityEntry> Entries);

/// <summary>One product of a SECOM <c>Capability</c> response.</summary>
/// <param name="ContainerType">The container type offered.</param>
/// <param name="ProductSpec">The data product in canonical short form, if stated.</param>
/// <param name="ServiceVersion">The SECOM edition the service implements (<c>serviceVersion</c>).</param>
/// <param name="Get">True when <c>Get</c> is implemented.</param>
/// <param name="GetSummary">True when <c>GetSummary</c> is implemented.</param>
public sealed record SecomCapabilityEntry(
    SecomContainerType ContainerType, string? ProductSpec, string? ServiceVersion, bool Get, bool GetSummary);

/// <summary>A data object returned by SECOM <c>Get</c>.</summary>
/// <param name="Data">The decoded (base64) data, still encrypted or compressed if <paramref name="Metadata"/> says so.</param>
/// <param name="Metadata">The object's exchange metadata, including its signature, if any.</param>
public sealed record SecomDataObject(byte[] Data, SecomExchangeMetadata? Metadata);

/// <summary>The SECOM <c>exchangeMetadata</c> of a data object.</summary>
/// <param name="DataProtection">True when the data is encrypted.</param>
/// <param name="ProtectionScheme">The protection scheme (<c>SECOM</c> or <c>S100</c>), if stated.</param>
/// <param name="SignatureReference">The signature algorithm (<c>digitalSignatureReference</c>, e.g. <c>ecdsa-384-sha2</c>).</param>
/// <param name="Compressed">True when the data is compressed (<c>compressionFlag</c>).</param>
/// <param name="PublicCertificates">The signer's certificate first, then any others, as the service sent them (base64 DER or PEM).</param>
/// <param name="RootCertificateThumbprint">The hex thumbprint of the signer's root certificate, if stated.</param>
/// <param name="Signature">The hex-encoded signature over the data, if any.</param>
public sealed record SecomExchangeMetadata(
    bool DataProtection,
    string? ProtectionScheme,
    string? SignatureReference,
    bool Compressed,
    IReadOnlyList<string> PublicCertificates,
    string? RootCertificateThumbprint,
    string? Signature);

/// <summary>Narrows a SECOM <c>GetSummary</c> request.</summary>
/// <param name="GeometryWkt">A WKT polygon or line string the objects must intersect, or <see langword="null"/>.</param>
/// <param name="ValidFrom">The start of the validity window, or <see langword="null"/>.</param>
/// <param name="ValidTo">The end of the validity window, or <see langword="null"/>.</param>
/// <param name="PageSize">How many entries to ask for per page.</param>
public sealed record SecomQuery(
    string? GeometryWkt = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null,
    int PageSize = 100)
{
    /// <summary>A query for everything the service offers.</summary>
    public static SecomQuery All { get; } = new();
}
