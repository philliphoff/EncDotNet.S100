# EncDotNet.S100.ExchangeSets

Reader for S-100 Exchange Set catalogues, dataset/support file discovery, and digital signature verification.

## Overview

This library parses S-100 Exchange Set `CATALOG.XML` files and provides access to the datasets and support files within an exchange set. Key types include:

- **`ExchangeSet`** — opens and navigates an exchange set through an `IAssetSource`.
- **`ExchangeCatalogue`** — the parsed catalogue metadata.
- **`ExchangeCatalogueReader`** — XML parser for the exchange catalogue.
- **`DatasetDiscoveryMetadata`** — metadata for each dataset in the exchange set (file name, bounding box, product specification).
- **`SupportFileDiscoveryMetadata`** — metadata for support files.
- **`CatalogueDiscoveryMetadata`** — metadata for embedded catalogues.

## File path resolution

Producers lay out exchange sets in different ways, and the catalogue can
describe a file's location in several forms. `ExchangeSet` normalizes all
of them into a single source-relative path:

- A separate `<filePath>` directory element combined with a bare
  `<fileName>` (e.g. UKHO S-101: `filePath=101GB00502793`,
  `fileName=101GB00502793.000`).
- A full path folded into `<fileName>`, optionally with a `file:/` URI
  prefix (e.g. `file:/S-101/DATASET_FILES/101AU005BTB01.000`).
- Windows-style separators and a leading slash in `<filePath>`
  (e.g. NOAA S-102: `\S102\PBC_UTM11N_MLLW_LALB`).

Use **`DatasetDiscoveryMetadata.RelativePath`** (and the equivalent on the
support/catalogue metadata types) — or the static
`ExchangeSet.ResolveRelativePath(filePath, fileName)` — to obtain the path
to pass to an `IAssetSource`. `ExchangeSet.NormalizeFileName` handles the
`file:/` prefix, backslash separators, and leading slashes on a bare file
name.

The reader also recognizes dataset/support/catalogue discovery items that
are wrapped in product-specific elements and namespaces
(e.g. `S102_DatasetDiscoveryMetadata` in `http://www.iho.int/s102/2.0/xc`),
not just the generic `S100_DatasetDiscoveryMetadata`.

## Digital Signature Verification

The library implements the S-100 Part 15 Data Protection Scheme for **signature verification**. Exchange sets may include per-file digital signatures (DSA or ECDSA P-256 over SHA-256) embedded in `CATALOG.XML`, along with a certificate block referencing the signing data provider.

### Model types

| Type | Description | S-100 Part 15 ref |
|---|---|---|
| `DigitalSignatureAlgorithm` | Enum: `DSA`, `ECDSA` | §15-4.1 |
| `DigitalSignatureValue` | Parsed `S100_SE_DigitalSignature` (id, certificateRef, raw signature bytes) | §15-4.2 |
| `CertificateBlock` | Certificate collection from the catalogue (scheme administrator ID + certificate entries) | §15-5 |
| `CertificateEntry` | Individual X.509 certificate (id, issuer, DER-encoded bytes) | §15-5.2 |

These are surfaced as properties on `DatasetDiscoveryMetadata`, `SupportFileDiscoveryMetadata`, `CatalogueDiscoveryMetadata` (via `DigitalSignatureAlgorithm` and `DigitalSignatureValue?`), and on `ExchangeCatalogue` (via `CertificateBlock?`).

### Verification API

```csharp
// Create a verifier
IExchangeSetVerifier verifier = new ExchangeSetVerifier();

// Configure trust anchors (optional — pass trusted SA root certificates)
var trust = new TrustAnchorOptions
{
    // For development/testing, skip certificate chain validation:
    AllowUntrustedCertificates = true,

    // For production, supply IHO SA root certificates:
    // TrustedRoots = [saRootCert],
};

// Verify an exchange set
ExchangeSetVerificationResult result = await verifier.VerifyAsync(
    assetSource,    // IAssetSource (filesystem or ZIP)
    catalogue,      // ExchangeCatalogue (from ExchangeCatalogueReader)
    trust,
    cancellationToken);

// Inspect results
if (result.IsUnsigned)
{
    // No signatures present — exchange set is unsigned
}
else if (result.AllValid)
{
    // All files have valid signatures
}
else if (result.HasInvalidSignatures)
{
    // At least one file has an invalid or untrusted signature
    foreach (var file in result.FileResults)
    {
        Console.WriteLine($"{file.FileName}: {file.Outcome} — {file.Detail}");
    }
}
```

### Verification outcomes

| `VerificationOutcome` | Meaning |
|---|---|
| `Ok` | Signature is valid and certificate is trusted (or trust check skipped) |
| `NotSigned` | No digital signature present for this file |
| `SignatureInvalid` | Signature does not match the file contents |
| `CertificateUntrusted` | Signature is valid but the certificate is not trusted |
| `CertificateExpired` | Certificate has expired |
| `FileMissing` | Referenced file not found in the asset source |
| `Error` | Unexpected error during verification |

### Trust anchor model

`TrustAnchorOptions` controls how certificate trust is evaluated:

- **`TrustedRoots`** — a list of `X509Certificate2` instances representing trusted Scheme Administrator (SA) root certificates. A signing certificate's `Issuer` field is matched against these roots.
- **`AllowUntrustedCertificates`** — when `true`, signatures are verified for correctness but certificate chain validation is skipped. This is useful during development or when loading exchange sets from unknown sources.

The IHO publishes test SA certificates for interoperability testing. For production use, supply the official IHO SA root certificate.

### Scope and limitations

- **Verification only** — signing/authoring of exchange sets is not yet implemented.
- **Encryption is out of scope** — Part 15 §3 Confidentiality (ENC permits, cell-level decryption) is not supported.
- File hashing uses streaming SHA-256 to avoid loading large HDF5 files into memory.

## Installation

```sh
dotnet add package EncDotNet.S100.ExchangeSets
```
